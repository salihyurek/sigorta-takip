// Single source of truth for the client-side password rules. Mirrors the server's
// ValidatePasswordStrength (BaseApiController.cs) — keep the two in sync so the UI
// never accepts a password the server will reject (or vice versa).
export function validatePassword(password: string): string | null {
  if (password.length < 8) {
    return 'Şifre en az 8 karakter uzunluğunda olmalıdır.';
  }
  // BCrypt only considers the first 72 bytes; the server rejects longer ones.
  if (new TextEncoder().encode(password).length > 72) {
    return 'Şifre çok uzun (en fazla 72 karakter olmalıdır).';
  }
  if (!/[A-Z]/.test(password)) {
    return 'Şifre en az bir büyük harf içermelidir.';
  }
  if (!/[a-z]/.test(password)) {
    return 'Şifre en az bir küçük harf içermelidir.';
  }
  if (!/[0-9]/.test(password)) {
    return 'Şifre en az bir rakam içermelidir.';
  }
  return null;
}
