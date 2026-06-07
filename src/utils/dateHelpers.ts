// Single source of truth for the "expiring soon" window (in days). The status
// colours, the dashboard count/labels and the table filter all derive from this,
// so it can never drift between them. Keep it aligned with the largest backend
// REMINDER_DAYS threshold (default 15) so the UI warns when emails start going out.
export const SOON_THRESHOLD_DAYS = 15;

// Get today's local date in YYYY-MM-DD format
export function getTodayString(): string {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Parse a YYYY-MM-DD string as a LOCAL calendar date (midnight local time).
// Using `new Date('YYYY-MM-DD')` would parse as UTC midnight, which shifts the day
// for non-UTC users and causes off-by-one errors near midnight.
function parseLocalDate(dateStr: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateStr);
  if (!m) {
    const fallback = new Date(dateStr);
    return isNaN(fallback.getTime()) ? null : fallback;
  }
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

// Calculate days remaining between today and the end date
export function getDaysRemaining(endDateStr: string): number {
  if (!endDateStr) return 0;

  const today = new Date();
  // Set time components to midnight to compare only dates
  today.setHours(0, 0, 0, 0);

  const end = parseLocalDate(endDateStr);
  if (!end) return 0;
  end.setHours(0, 0, 0, 0);

  const diffTime = end.getTime() - today.getTime();
  const diffDays = Math.round(diffTime / (1000 * 60 * 60 * 24));

  return diffDays;
}

// Classify policy status based on days remaining. The "soon" window defaults to
// SOON_THRESHOLD_DAYS but callers can pass the value fetched from /api/config so the
// UI matches the backend's REMINDER_DAYS.
export function getPolicyStatus(
  endDateStr: string,
  soonThreshold: number = SOON_THRESHOLD_DAYS
): 'expired' | 'today' | 'soon' | 'active' {
  if (!endDateStr) return 'expired';

  const days = getDaysRemaining(endDateStr);

  if (days < 0) return 'expired';
  if (days === 0) return 'today';
  if (days <= soonThreshold) return 'soon';
  return 'active';
}

// Format YYYY-MM-DD to DD.MM.YYYY
export function formatDate(dateStr: string): string {
  if (!dateStr) return '-';
  const parts = dateStr.split('-');
  if (parts.length !== 3) return dateStr;
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
}
