import assert from 'node:assert/strict'
import test from 'node:test'
import { calendarDateLabel } from '../src/i18n/calendar.ts'

test('Thai build labels use Buddhist years without altering their date or version identity', () => {
  assert.equal(calendarDateLabel('China 2013-12-18', 'th'), 'China 2556-12-18')
  assert.equal(calendarDateLabel('Korea 2015-01-22 (kr-812)', 'th-TH'), 'Korea 2558-01-22 (kr-812)')
  assert.equal(calendarDateLabel('2000-02-29 00:00:00', 'th'), '2543-02-29 00:00:00')
  assert.equal(calendarDateLabel('2011-04-08 / 2019-09-30', 'th'), '2554-04-08 / 2562-09-30')
})

test('other UI languages retain Gregorian labels and invalid dates remain unchanged', () => {
  for (const locale of ['zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'en']) {
    assert.equal(calendarDateLabel('China 2013-12-18', locale), 'China 2013-12-18')
  }
  for (const value of ['2013-02-29', '2013-13-01', '2013-04-31', '2013-00-00', 'kr-812', '0.1.60']) {
    assert.equal(calendarDateLabel(value, 'th'), value)
  }
})
