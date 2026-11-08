import test from 'node:test';
import assert from 'node:assert/strict';
import { waitForNativeSurface } from '../../src/FantasyTale.Launcher.Overlay/overlay/surface-ready.js';

class Observer {
  static latest;
  constructor(callback) { this.callback = callback; Observer.latest = this; }
  observe() {}
  disconnect() { this.disconnected = true; }
}
globalThis.MutationObserver = Observer;

test('Vue containers alone do not complete native resource readiness', async () => {
  const container = { firstElementChild: null };
  const waiter = waitForNativeSurface({ querySelector: () => container });
  const observer = Observer.latest;
  let ready = false;
  waiter.ready.then(() => { ready = true; });
  observer.callback();
  await Promise.resolve();
  assert.equal(ready, false);
  container.firstElementChild = {};
  observer.callback();
  await waiter.ready;
  assert.equal(ready, true);
  assert.equal(observer.disconnected, true);
});

test('resource failure rejects mounting and releases its observer', async () => {
  const waiter = waitForNativeSurface({ querySelector: () => null });
  const observer = Observer.latest;
  waiter.fail(new Error('Missing theme manifest'));
  await assert.rejects(waiter.ready, /Missing theme manifest/);
  assert.equal(observer.disconnected, true);
});

test('stalled native resources have a finite readiness deadline', async () => {
  const waiter = waitForNativeSurface({ querySelector: () => null }, 5);
  await assert.rejects(waiter.ready, /did not become ready/);
  assert.equal(Observer.latest.disconnected, true);
});
