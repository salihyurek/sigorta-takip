const cron = require('node-cron');
const db = require('./db');
const { sendExpiryAlert } = require('./mail');

// Helper to get today's date in YYYY-MM-DD format based on server time
function getTodayString() {
  const date = new Date();
  
  // Format as YYYY-MM-DD using timezone offset or simple ISO split
  // Using local time zone dates
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  
  return `${year}-${month}-${day}`;
}

// Function to scan database and send emails for expiring policies
async function checkAllExpiries() {
  console.log(`[Scheduler] Checking for expiring policies. Current time: ${new Date().toISOString()}`);
  
  const data = db.readDb();
  const settings = data.settings;
  
  if (!settings || !settings.enableEmails) {
    console.log('[Scheduler] Email notifications are disabled in settings. Skipping check.');
    return { success: true, message: 'Email notifications disabled.', sentCount: 0 };
  }

  const todayStr = getTodayString();
  console.log(`[Scheduler] Checking for policies expiring today: ${todayStr}`);
  
  let emailsSent = 0;
  let hasChanges = false;

  for (let bus of data.buses) {
    for (let policyKey of ['trafik', 'kasko', 'koltuk']) {
      const policy = bus.policies[policyKey];
      if (policy && policy.endDate === todayStr) {
        // Check if we already sent an email for this policy today
        if (policy.lastEmailedDate !== todayStr) {
          console.log(`[Scheduler] Policy ${policyKey} for bus ${bus.plate} expires today! Sending email...`);
          try {
            await sendExpiryAlert(bus, policyKey, todayStr, settings);
            policy.lastEmailedDate = todayStr;
            emailsSent++;
            hasChanges = true;
            console.log(`[Scheduler] Email sent successfully for ${bus.plate} - ${policyKey}`);
          } catch (err) {
            console.error(`[Scheduler] Failed to send email for ${bus.plate} - ${policyKey}:`, err.message);
          }
        } else {
          console.log(`[Scheduler] Policy ${policyKey} for bus ${bus.plate} expires today, but an email was already sent today.`);
        }
      }
    }
  }

  if (hasChanges) {
    db.writeDb(data);
    console.log('[Scheduler] Database updated with email timestamps.');
  }

  console.log(`[Scheduler] Expiry check complete. Sent ${emailsSent} email alerts.`);
  return { success: true, sentCount: emailsSent };
}

// Initialize the scheduler
function initScheduler() {
  // 1. Run check on startup after a small delay (10 seconds)
  setTimeout(() => {
    console.log('[Scheduler] Running initial startup check...');
    checkAllExpiries().catch(err => console.error('[Scheduler] Startup check failed:', err));
  }, 10000);

  // 2. Schedule cron job to run every day at 08:00 AM (local time)
  // '0 8 * * *' = at 08:00 every day
  cron.schedule('0 8 * * *', () => {
    console.log('[Scheduler] Running daily scheduled check (08:00 AM)...');
    checkAllExpiries().catch(err => console.error('[Scheduler] Daily check failed:', err));
  });

  // 3. (Optional) Schedule a check every 4 hours as a fallback for local servers that aren't on 24/7
  cron.schedule('0 0,4,12,16,20 * * *', () => {
    console.log('[Scheduler] Running periodic check (every 4 hours)...');
    checkAllExpiries().catch(err => console.error('[Scheduler] Periodic check failed:', err));
  });

  console.log('[Scheduler] Cron scheduler initialized.');
}

module.exports = {
  checkAllExpiries,
  initScheduler
};
