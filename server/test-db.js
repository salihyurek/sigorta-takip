const db = require('./db');

console.log('--- Sigorta Takip Database Integration Test (Roles & Password Reset) ---');

// 1. Initialize & Read
console.log('\nStep 1: Reading database...');
const data = db.readDb();
console.log(`Successfully read db. Found ${data.buses.length} buses and ${data.users.length} users.`);

// Verify superadmin default seeding
const superAdmin = data.users.find(u => u.email === db.SUPERADMIN_EMAIL);
if (superAdmin && superAdmin.role === 'superadmin') {
  console.log(`✓ Seeding successful: ${db.SUPERADMIN_EMAIL} exists with superadmin role.`);
} else {
  console.error(`✗ Seeding failed: ${db.SUPERADMIN_EMAIL} not found or incorrect role!`);
  process.exit(1);
}

// Verify old admin removal
const oldAdmin = data.users.find(u => u.email === 'admin@sigortatakip.com');
if (!oldAdmin) {
  console.log('✓ Old default admin (admin@sigortatakip.com) successfully removed for security.');
} else {
  console.error('✗ Old admin account still exists in database!');
  process.exit(1);
}

// 2. Verify Hashing of Owner Password
console.log('\nStep 2: Checking password hash for owner...');
if (db.comparePassword(db.SUPERADMIN_PASSWORD, superAdmin.password)) {
  console.log('✓ Password hash validates the owner\'s secure password.');
} else {
  console.error('✗ Owner password hash mismatch!');
  process.exit(1);
}

// 3. User Role Delegation
console.log('\nStep 3: Creating a Viewer user...');
const initialUserCount = data.users.length;
const viewerEmail = 'viewer_test@acente.com';
const viewerPass = 'Viewer456';

const newViewer = {
  id: 'user-viewer-' + Date.now(),
  email: viewerEmail,
  password: db.hashPassword(viewerPass),
  role: 'viewer', // Added managers default to viewers
  resetToken: null,
  resetTokenExpiry: null
};

data.users.push(newViewer);
db.writeDb(data);

// Verify creation and role
const data2 = db.readDb();
const createdViewer = data2.users.find(u => u.email === viewerEmail);
if (createdViewer && createdViewer.role === 'viewer') {
  console.log('✓ Create Viewer successful: User seeded with viewer role.');
} else {
  console.error('✗ Create Viewer failed!');
  process.exit(1);
}

// 4. Password Recovery flow simulation
console.log('\nStep 4: Simulating password recovery (Forgot Password)...');
const data3 = db.readDb();
const targetUser = data3.users.find(u => u.email === viewerEmail);

// Generate dummy token
const mockToken = 'mock_reset_token_hex_12345';
const mockExpiry = Date.now() + 3600000; // 1 hour

targetUser.resetToken = mockToken;
targetUser.resetTokenExpiry = mockExpiry;
db.writeDb(data3);

// Verify token save
const data4 = db.readDb();
const userWithToken = data4.users.find(u => u.email === viewerEmail);
if (userWithToken && userWithToken.resetToken === mockToken && userWithToken.resetTokenExpiry > Date.now()) {
  console.log('✓ Forgot Password simulation: Recovery token and expiration registered.');
} else {
  console.error('✗ Forgot Password simulation failed!');
  process.exit(1);
}

// Simulate Password Reset completion
console.log('\nStep 5: Completing password reset...');
const data5 = db.readDb();
const resettingUser = data5.users.find(
  u => u.resetToken === mockToken && u.resetTokenExpiry > Date.now()
);

if (resettingUser) {
  const finalPass = 'new_secure_pass_99';
  resettingUser.password = db.hashPassword(finalPass);
  resettingUser.resetToken = null;
  resettingUser.resetTokenExpiry = null;
  db.writeDb(data5);
  console.log('✓ Reset simulation: New hashed password saved, tokens cleared.');
} else {
  console.error('✗ Reset simulation failed: Token match not found!');
  process.exit(1);
}

// Verify password updated and token is null
const data6 = db.readDb();
const verifiedUser = data6.users.find(u => u.email === viewerEmail);
if (verifiedUser && db.comparePassword('new_secure_pass_99', verifiedUser.password) && verifiedUser.resetToken === null) {
  console.log('✓ Reset Verification: Password updated, token fields set to null.');
} else {
  console.error('✗ Reset Verification failed!');
  process.exit(1);
}

// 5. Clean up Viewer user
console.log('\nStep 6: Deleting test viewer user...');
const data7 = db.readDb();
data7.users = data7.users.filter(u => u.email !== viewerEmail);
db.writeDb(data7);

const finalData = db.readDb();
if (finalData.users.length === initialUserCount) {
  console.log('✓ Clean up successful: Database returned to initial count.');
} else {
  console.error('✗ Clean up failed!');
  process.exit(1);
}

console.log('\n--- All RBAC & Password Reset Integration Tests Passed successfully! ---');
process.exit(0);
