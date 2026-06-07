export interface PolicyDetail {
  startDate: string;
  endDate: string;
  lastEmailedDate: string | null;
  lastNotifiedThreshold?: number | null;
}

export interface Bus {
  id: string;
  plate: string;
  brand: string;
  operator: string;
  policies: {
    trafik: PolicyDetail;
    kasko: PolicyDetail;
    koltuk: PolicyDetail;
    [key: string]: PolicyDetail; // Allow dynamic key access
  };
}

export interface SMTPConfig {
  smtpHost: string;
  smtpPort: number;
  smtpUser: string;
  smtpPass: string;
  senderName?: string;
  senderEmail: string;
  receiverEmail: string;
  enableEmails: boolean;
}

export interface BackupData {
  settings?: SMTPConfig;
  buses: Bus[];
}
