// Get today's local date in YYYY-MM-DD format
export function getTodayString(): string {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Calculate days remaining between today and the end date
export function getDaysRemaining(endDateStr: string): number {
  if (!endDateStr) return 0;
  
  const today = new Date();
  // Set time components to midnight to compare only dates
  today.setHours(0, 0, 0, 0);
  
  const end = new Date(endDateStr);
  end.setHours(0, 0, 0, 0);
  
  const diffTime = end.getTime() - today.getTime();
  const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
  
  return diffDays;
}

// Classify policy status based on days remaining
export function getPolicyStatus(endDateStr: string): 'expired' | 'today' | 'soon' | 'active' {
  if (!endDateStr) return 'expired';
  
  const days = getDaysRemaining(endDateStr);
  
  if (days < 0) return 'expired';
  if (days === 0) return 'today';
  if (days <= 15) return 'soon';
  return 'active';
}

// Format YYYY-MM-DD to DD.MM.YYYY
export function formatDate(dateStr: string): string {
  if (!dateStr) return '-';
  const parts = dateStr.split('-');
  if (parts.length !== 3) return dateStr;
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
}
