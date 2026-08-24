// The IndexedDB path, run over fake-indexeddb - a spec-level in-memory implementation of the
// real API, so transactions, key ranges and version upgrades behave as in a browser.
import 'fake-indexeddb/auto';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createStorage, DRIVER_INDEXED_DB, DRIVER_MEMORY } from '../../src/Blazor.IndexedDB/wwwroot/indexeddb-storage.js';

const encoder = new TextEncoder();
let databaseCounter = 0;

function uniqueDatabaseName() {
    databaseCounter += 1;
    return `storage-test-${databaseCounter}`;
}

function bytes(text) {
    return encoder.encode(text);
}

function openRaw(databaseName, version, onUpgrade) {
    return new Promise((resolve, reject) => {
        const request = version === undefined ? indexedDB.open(databaseName) : indexedDB.open(databaseName, version);
        request.onupgradeneeded = (event) => onUpgrade?.(event.target.result);
        request.onsuccess = (event) => resolve(event.target.result);
        request.onerror = (event) => reject(event.target.error);
    });
}

test('probe reports the IndexedDB driver when the database opens', async () => {
    const storage = createStorage(uniqueDatabaseName());

    assert.equal(await storage.probe(), DRIVER_INDEXED_DB);
});

test('a fresh database gets the records store created', async () => {
    const databaseName = uniqueDatabaseName();
    const storage = createStorage(databaseName);
    await storage.probe();

    const db = await openRaw(databaseName);
    assert.equal(db.version, 1);
    assert.ok(db.objectStoreNames.contains('records'));
    db.close();
});

test('an existing database without the records store is upgraded to gain it', async () => {
    const databaseName = uniqueDatabaseName();
    const legacyDb = await openRaw(databaseName, 1, (db) => db.createObjectStore('other'));
    legacyDb.close();

    const storage = createStorage(databaseName);
    await storage.set('settings', 'theme', bytes('dark'), 'relaxed');

    const db = await openRaw(databaseName);
    assert.equal(db.version, 2);
    assert.ok(db.objectStoreNames.contains('other'), 'existing stores must survive the upgrade');
    assert.ok(db.objectStoreNames.contains('records'));
    db.close();
    assert.deepEqual(await storage.get('settings', 'theme'), bytes('dark'));
});

test('set and get round-trip bytes, and a missing key reads as null', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('settings', 'theme', bytes('{"mode":"dark"}'), 'relaxed');

    assert.deepEqual(await storage.get('settings', 'theme'), bytes('{"mode":"dark"}'));
    assert.equal(await storage.get('settings', 'missing'), null);
});

test('set replaces an existing value', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('settings', 'theme', bytes('dark'), 'relaxed');
    await storage.set('settings', 'theme', bytes('light'), 'strict');

    assert.deepEqual(await storage.get('settings', 'theme'), bytes('light'));
});

test('containsKey and remove', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('settings', 'theme', bytes('dark'), 'relaxed');
    assert.equal(await storage.containsKey('settings', 'theme'), true);

    await storage.remove('settings', 'theme', 'relaxed');
    assert.equal(await storage.containsKey('settings', 'theme'), false);
    await storage.remove('settings', 'theme', 'relaxed');
});

test('keys lists only the store own entries, even for a store whose name prefixes another', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('auth', 'access', bytes('a'), 'relaxed');
    await storage.set('auth', 'refresh', bytes('r'), 'relaxed');
    await storage.set('authx', 'other', bytes('o'), 'relaxed');
    await storage.replaceCollection('auth', bytes('[]'), 'relaxed');

    assert.deepEqual((await storage.keys('auth')).sort(), ['access', 'refresh']);
    assert.deepEqual(await storage.keys('authx'), ['other']);
    assert.deepEqual(await storage.keys('empty'), []);
});

test('a store collection is independent of its key/value entries', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('exercises', 'version', bytes('7'), 'relaxed');
    await storage.replaceCollection('exercises', bytes('[1,2,3]'), 'relaxed');
    await storage.replaceCollection('exercises', bytes('[4]'), 'strict');

    assert.deepEqual(await storage.getCollection('exercises'), bytes('[4]'));
    assert.deepEqual(await storage.get('exercises', 'version'), bytes('7'), 'entries survive a collection replace');
    assert.equal(await storage.getCollection('never-written'), null);
});

test('clear wipes the store entries and its collection, and nothing else', async () => {
    const storage = createStorage(uniqueDatabaseName());

    await storage.set('cache', 'a', bytes('1'), 'relaxed');
    await storage.replaceCollection('cache', bytes('[]'), 'relaxed');
    await storage.set('settings', 'theme', bytes('dark'), 'relaxed');

    await storage.clear('cache', 'relaxed');

    assert.equal(await storage.get('cache', 'a'), null);
    assert.equal(await storage.getCollection('cache'), null);
    assert.deepEqual(await storage.get('settings', 'theme'), bytes('dark'));
});

test('a second storage over the same database sees the persisted data', async () => {
    const databaseName = uniqueDatabaseName();
    await createStorage(databaseName).set('settings', 'theme', bytes('dark'), 'relaxed');

    assert.deepEqual(await createStorage(databaseName).get('settings', 'theme'), bytes('dark'));
});

test('a database that stops opening mid-session degrades to memory and stays there', async () => {
    const storage = createStorage(uniqueDatabaseName());
    await storage.set('settings', 'theme', bytes('dark'), 'relaxed');
    assert.equal(await storage.probe(), DRIVER_INDEXED_DB);

    const realIndexedDb = globalThis.indexedDB;
    globalThis.indexedDB = { open() { throw new Error('Connection to Indexed Database server lost'); } };
    try {
        await storage.set('settings', 'locale', bytes('sk'), 'relaxed');

        assert.equal(await storage.probe(), DRIVER_MEMORY);
        assert.deepEqual(await storage.get('settings', 'locale'), bytes('sk'));
    } finally {
        globalThis.indexedDB = realIndexedDb;
    }

    assert.equal(await storage.probe(), DRIVER_MEMORY, 'the driver choice is sticky for the session');
});
