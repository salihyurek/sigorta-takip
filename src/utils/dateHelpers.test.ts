import { test } from 'node:test';
import assert from 'node:assert/strict';
import { getTodayString, getDaysRemaining, getPolicyStatus, formatDate } from './dateHelpers.ts';

// Build a YYYY-MM-DD string for a local date `offset` days from today.
function localDateString(offset: number): string {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

test('getTodayString returns local today as YYYY-MM-DD', () => {
  assert.equal(getTodayString(), localDateString(0));
});

test('formatDate converts ISO to DD.MM.YYYY', () => {
  assert.equal(formatDate('2026-06-07'), '07.06.2026');
});

test('formatDate handles empty and malformed input', () => {
  assert.equal(formatDate(''), '-');
  assert.equal(formatDate('invalid'), 'invalid');
});

test('getDaysRemaining counts whole days correctly', () => {
  assert.equal(getDaysRemaining(localDateString(0)), 0);
  assert.equal(getDaysRemaining(localDateString(5)), 5);
  assert.equal(getDaysRemaining(localDateString(-3)), -3);
  assert.equal(getDaysRemaining(localDateString(15)), 15);
});

test('getDaysRemaining for today is exactly 0 regardless of timezone (local-parse fix)', () => {
  // Previously the date was parsed as UTC midnight, which produced -1 in
  // positive-offset timezones. The local-parse fix keeps this at 0.
  assert.equal(getDaysRemaining(localDateString(0)), 0);
});

test('getPolicyStatus classifies by days remaining', () => {
  assert.equal(getPolicyStatus(localDateString(-1)), 'expired');
  assert.equal(getPolicyStatus(localDateString(0)), 'today');
  assert.equal(getPolicyStatus(localDateString(1)), 'soon');
  assert.equal(getPolicyStatus(localDateString(15)), 'soon');
  assert.equal(getPolicyStatus(localDateString(16)), 'active');
  assert.equal(getPolicyStatus(''), 'expired');
});
