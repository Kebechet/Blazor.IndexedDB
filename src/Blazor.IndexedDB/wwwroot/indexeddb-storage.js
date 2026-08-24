/**
 * Key/value and collection storage over one IndexedDB database.
 *
 * One object store ("records") holds every value of the database. A record is
 * { ck: <composite key>, value: <bytes> } where the composite key is `storeName + " " + key`,
 * so a whole store - a logical namespace - is one key range that can be listed, replaced or
 * cleared in a single transaction. A store's collection lives under a reserved key of that
 * same range.
 *
 * A browser can run a whole session without a working IndexedDB (WKWebView leaves
 * window.indexedDB undefined after some background launches). When the database cannot be
 * opened the same API keeps working over an in-memory Map for the rest of the session, so a
 * caller only ever sees a miss, never an exception.
 */

const STORE = 'records';
const SEPARATOR = ' ';
const RANGE_UPPER = '￿';
const COLLECTION_KEY = '__all__';

export const DRIVER_INDEXED_DB = 'indexedDb';
export const DRIVER_MEMORY = 'memory';

/**
 * Creates a storage bound to one database. The database is opened per operation and closed
 * again, so no connection outlives the operation that needed it.
 */
export function createStorage(databaseName) {
    let driver = null;
    const memoryRecords = new Map();

    function compositeKey(storeName, key) {
        return storeName + SEPARATOR + key;
    }

    function storePrefix(storeName) {
        return storeName + SEPARATOR;
    }

    function storeRange(storeName) {
        const prefix = storePrefix(storeName);
        return IDBKeyRange.bound(prefix, prefix + RANGE_UPPER);
    }

    function isCollectionKey(compositeKeyValue, storeName) {
        return compositeKeyValue === compositeKey(storeName, COLLECTION_KEY);
    }

    function requestToPromise(request) {
        return new Promise((resolve, reject) => {
            request.onsuccess = (event) => resolve(event.target.result);
            request.onerror = (event) => reject(event.target.error);
        });
    }

    function transactionDone(transaction) {
        return new Promise((resolve, reject) => {
            transaction.oncomplete = () => resolve();
            transaction.onerror = (event) => reject(event.target.error);
            transaction.onabort = (event) => reject(event.target.error);
        });
    }

    function openRequest(request) {
        return new Promise((resolve, reject) => {
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                if (!db.objectStoreNames.contains(STORE)) {
                    db.createObjectStore(STORE, { keyPath: 'ck' });
                }
            };
            request.onsuccess = (event) => resolve(event.target.result);
            request.onerror = (event) => reject(event.target.error);
            request.onblocked = () => reject(new Error(`Opening database '${databaseName}' is blocked by another connection.`));
        });
    }

    /* Opens the database at whatever version it has. A database that exists without this
       module's object store (created by other code) is upgraded by one version to add it. */
    async function openDb() {
        if (typeof indexedDB === 'undefined') {
            throw new Error("Can't find variable: indexedDB");
        }

        const db = await openRequest(indexedDB.open(databaseName));
        if (db.objectStoreNames.contains(STORE)) {
            return db;
        }

        const nextVersion = db.version + 1;
        db.close();
        return openRequest(indexedDB.open(databaseName, nextVersion));
    }

    function degradeToMemory(error) {
        console.warn(`indexeddb-storage: IndexedDB is unavailable for '${databaseName}', storage runs in memory for this session.`, error);
        driver = DRIVER_MEMORY;
    }

    /* Picks the driver once per session and reports it. */
    async function probe() {
        if (driver !== null) {
            return driver;
        }

        try {
            const db = await openDb();
            db.close();
            driver = DRIVER_INDEXED_DB;
        } catch (error) {
            degradeToMemory(error);
        }

        return driver;
    }

    /* Runs the IndexedDB operation against a fresh connection, or the in-memory equivalent once
       degraded. Only a failure to open the database switches drivers; an error inside an
       operation still reaches the caller. */
    async function run(indexedDbOperation, memoryOperation) {
        if ((await probe()) === DRIVER_MEMORY) {
            return memoryOperation();
        }

        let db;
        try {
            db = await openDb();
        } catch (error) {
            degradeToMemory(error);
            return memoryOperation();
        }

        try {
            return await indexedDbOperation(db);
        } finally {
            db.close();
        }
    }

    function memoryCompositeKeys(storeName) {
        const prefix = storePrefix(storeName);
        return Array.from(memoryRecords.keys()).filter((compositeKeyValue) => compositeKeyValue.startsWith(prefix));
    }

    function get(storeName, key) {
        const ck = compositeKey(storeName, key);
        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readonly');
                const record = await requestToPromise(transaction.objectStore(STORE).get(ck));
                return record === undefined ? null : record.value;
            },
            () => {
                const value = memoryRecords.get(ck);
                return value === undefined ? null : value;
            });
    }

    function set(storeName, key, value, durability) {
        const ck = compositeKey(storeName, key);
        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readwrite', { durability: durability });
                transaction.objectStore(STORE).put({ ck: ck, value: value });
                await transactionDone(transaction);
            },
            () => {
                memoryRecords.set(ck, value);
            });
    }

    function remove(storeName, key, durability) {
        const ck = compositeKey(storeName, key);
        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readwrite', { durability: durability });
                transaction.objectStore(STORE).delete(ck);
                await transactionDone(transaction);
            },
            () => {
                memoryRecords.delete(ck);
            });
    }

    function containsKey(storeName, key) {
        const ck = compositeKey(storeName, key);
        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readonly');
                const foundKey = await requestToPromise(transaction.objectStore(STORE).getKey(ck));
                return foundKey !== undefined;
            },
            () => memoryRecords.has(ck));
    }

    function keys(storeName) {
        const prefixLength = storePrefix(storeName).length;
        const toKeys = (compositeKeys) => compositeKeys
            .filter((compositeKeyValue) => !isCollectionKey(compositeKeyValue, storeName))
            .map((compositeKeyValue) => compositeKeyValue.substring(prefixLength));

        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readonly');
                const compositeKeys = await requestToPromise(transaction.objectStore(STORE).getAllKeys(storeRange(storeName)));
                return toKeys(compositeKeys);
            },
            () => toKeys(memoryCompositeKeys(storeName)));
    }

    function getCollection(storeName) {
        return get(storeName, COLLECTION_KEY);
    }

    function replaceCollection(storeName, value, durability) {
        return set(storeName, COLLECTION_KEY, value, durability);
    }

    function clear(storeName, durability) {
        return run(
            async (db) => {
                const transaction = db.transaction(STORE, 'readwrite', { durability: durability });
                transaction.objectStore(STORE).delete(storeRange(storeName));
                await transactionDone(transaction);
            },
            () => {
                for (const compositeKeyValue of memoryCompositeKeys(storeName)) {
                    memoryRecords.delete(compositeKeyValue);
                }
            });
    }

    return { probe, get, set, remove, containsKey, keys, getCollection, replaceCollection, clear };
}
