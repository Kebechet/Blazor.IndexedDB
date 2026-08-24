// The in-memory fallback, run in a process that has no indexedDB global at all - the exact state
// a WKWebView is in when it reports "Can't find variable: indexedDB".
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createStorage, DRIVER_MEMORY } from '../../src/Blazor.IndexedDB/wwwroot/indexeddb-storage.js';

const encoder = new TextEncoder();

function bytes(text) {
    return encoder.encode(text);
}

test('the process under test really has no indexedDB', () => {
    assert.equal(typeof indexedDB, 'undefined');
});

test('probe degrades to the memory driver and the choice is sticky', async () => {
    const storage = createStorage('memory-db');

    assert.equal(await storage.probe(), DRIVER_MEMORY);
    assert.equal(await storage.probe(), DRIVER_MEMORY);
});

test('the whole key/value API works from memory', async () => {
    const storage = createStorage('memory-db');

    assert.equal(await storage.get('settings', 'theme'), null);
    await storage.set('settings', 'theme', bytes('dark'), 'strict');
    assert.deepEqual(await storage.get('settings', 'theme'), bytes('dark'));
    assert.equal(await storage.containsKey('settings', 'theme'), true);

    await storage.remove('settings', 'theme', 'relaxed');
    assert.equal(await storage.containsKey('settings', 'theme'), false);
});

test('keys, collections and clear keep stores isolated from memory too', async () => {
    const storage = createStorage('memory-db');

    await storage.set('auth', 'access', bytes('a'), 'relaxed');
    await storage.set('authx', 'other', bytes('o'), 'relaxed');
    await storage.replaceCollection('auth', bytes('[1]'), 'relaxed');

    assert.deepEqual(await storage.keys('auth'), ['access']);
    assert.deepEqual(await storage.keys('authx'), ['other']);
    assert.deepEqual(await storage.getCollection('auth'), bytes('[1]'));

    await storage.clear('auth', 'relaxed');

    assert.equal(await storage.get('auth', 'access'), null);
    assert.equal(await storage.getCollection('auth'), null);
    assert.deepEqual(await storage.get('authx', 'other'), bytes('o'));
});

test('storages over different databases do not share memory', async () => {
    const first = createStorage('memory-db-1');
    const second = createStorage('memory-db-2');

    await first.set('settings', 'theme', bytes('dark'), 'relaxed');

    assert.equal(await second.get('settings', 'theme'), null);
});
