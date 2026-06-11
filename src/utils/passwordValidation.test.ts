import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validatePassword } from './passwordValidation.ts';

test('validatePassword accepts a strong password', () => {
  assert.equal(validatePassword('Sifre123'), null);
});

test('validatePassword rejects weak passwords', () => {
  assert.notEqual(validatePassword('Ab1'), null);       // too short
  assert.notEqual(validatePassword('sifre123'), null);  // no uppercase
  assert.notEqual(validatePassword('SIFRE123'), null);  // no lowercase
  assert.notEqual(validatePassword('SifreABC'), null);  // no digit
});

test('validatePassword enforces the 72-byte bcrypt limit like the server', () => {
  const ok72 = 'Aa1' + 'x'.repeat(69);      // exactly 72 ASCII bytes
  const tooLong = 'Aa1' + 'x'.repeat(70);   // 73 bytes
  assert.equal(validatePassword(ok72), null);
  assert.notEqual(validatePassword(tooLong), null);
});

test('validatePassword counts bytes, not characters (multibyte input)', () => {
  // 26 Turkish 'ş' chars = 52 bytes + 'Aa1' = 55 bytes → fine at 29 chars;
  // but 35 'ş' chars = 70 bytes + 'Aa1' = 73 bytes → over the limit.
  assert.equal(validatePassword('Aa1' + 'ş'.repeat(26)), null);
  assert.notEqual(validatePassword('Aa1' + 'ş'.repeat(35)), null);
});
