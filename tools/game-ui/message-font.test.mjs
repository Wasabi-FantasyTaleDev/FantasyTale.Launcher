import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

// The message body is drawn in the client's thin GB13000 face ('BF'); DF_GB_Y9 ('BFMalgun') is the heavier one and
// made overlay message boxes look bold next to the rest of the game UI.
test('message box body text uses the thin GB13000 face', () => {
  const source = readFileSync(new URL('./game-ui.js', import.meta.url), 'utf8');
  const start = source.indexOf('async function renderMessageBox(');
  const end = source.indexOf('async function dialog(', start);
  assert.ok(start >= 0 && end > start, 'renderMessageBox is present');
  const body = source.slice(start, end);
  assert.match(body, /fonts\.load\('400 13px BF'\)/);
  assert.match(body, /messageTextSize\(text,13,'BF'\)/);
  assert.match(body, /className='message-copy'[^\n]*family:'BF'/);
  assert.doesNotMatch(body, /BFMalgun/);
});
