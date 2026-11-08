import assert from 'node:assert/strict';
import {test} from 'node:test';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

test('an explicit missing private resource root fails instead of skipping',()=>{
  const environment={...process.env,FT_GAME_UI_RESOURCES:fileURLToPath(new URL('./private-resources.test.mjs/',import.meta.url))};
  // This child is an independent test run, not a worker of the parent runner.
  delete environment.NODE_TEST_CONTEXT;
  const result=spawnSync(process.execPath,['--test','--test-reporter=tap',fileURLToPath(new URL('./private-resources.test.mjs',import.meta.url))],{
    env:environment,
    encoding:'utf8',timeout:20000,windowsHide:true,
  });
  assert.ifError(result.error);
  assert.notEqual(result.status,0);
  assert.match(result.stdout,/fail 3/);
  assert.match(result.stdout,/skipped 0/);
});
