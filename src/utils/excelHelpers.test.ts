import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatExcelDate } from './excelHelpers.ts';

test('formatExcelDate parses Excel serial numbers as UTC (no off-by-one)', () => {
  // =DATE(2024,1,1) in Excel is serial 45292; the conversion must not shift a day
  // regardless of the host timezone (the bug was using local getters on a UTC date).
  assert.equal(formatExcelDate(45292), '2024-01-01');
  // =DATE(2026,6,7) is serial 46180.
  assert.equal(formatExcelDate(46180), '2026-06-07');
});

test('formatExcelDate accepts ISO strings unchanged', () => {
  assert.equal(formatExcelDate('2026-06-07'), '2026-06-07');
});

test('formatExcelDate parses Turkish DD.MM.YYYY and DD/MM/YYYY', () => {
  assert.equal(formatExcelDate('07.06.2026'), '2026-06-07');
  assert.equal(formatExcelDate('7/6/2026'), '2026-06-07');
});

test('formatExcelDate returns empty for blank/invalid input', () => {
  assert.equal(formatExcelDate(''), '');
  assert.equal(formatExcelDate(null), '');
  assert.equal(formatExcelDate(undefined), '');
  assert.equal(formatExcelDate('not a date'), '');
});
