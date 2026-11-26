-- GRAB & LOG full reset — grabandlog@gmail.com / member1@gmail.com
DROP TABLE IF EXISTS notifications CASCADE;
DROP TABLE IF EXISTS logs CASCADE;
DROP TABLE IF EXISTS tools CASCADE;
DROP TABLE IF EXISTS operators CASCADE;
DROP TABLE IF EXISTS users CASCADE;
DROP TABLE IF EXISTS transactions CASCADE;

CREATE TABLE operators (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name text NOT NULL,
  email text NOT NULL UNIQUE,
  pin text NOT NULL,
  role text NOT NULL DEFAULT 'member',
  rfid_uid text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX operators_rfid_uid_uidx ON operators (rfid_uid) WHERE rfid_uid IS NOT NULL;

CREATE TABLE tools (
  id text PRIMARY KEY,
  name text NOT NULL,
  icon text NOT NULL DEFAULT '🔧',
  status text NOT NULL DEFAULT 'available',
  location text NOT NULL DEFAULT '',
  logged_to text,
  borrowed_at timestamptz,
  borrowed_card_uid text
);

CREATE TABLE logs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  action text NOT NULL,
  tool_id text NOT NULL,
  tool_name text NOT NULL,
  user_name text NOT NULL,
  user_email text,
  details text,
  card_uid text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX logs_created_at_idx ON logs (created_at DESC);

CREATE TABLE notifications (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  type text NOT NULL,
  title text NOT NULL,
  message text NOT NULL,
  tool_id text,
  user_name text,
  for_role text NOT NULL DEFAULT 'all',
  is_read boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE operators ENABLE ROW LEVEL SECURITY;
ALTER TABLE tools ENABLE ROW LEVEL SECURITY;
ALTER TABLE logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
CREATE POLICY "allow all operators" ON operators FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY "allow all tools" ON tools FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY "allow all logs" ON logs FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY "allow all notifications" ON notifications FOR ALL USING (true) WITH CHECK (true);

INSERT INTO operators (name, email, pin, role, rfid_uid) VALUES
  ('Grab Admin', 'grabandlog@gmail.com', '1234', 'admin', NULL),
  ('Member 1', 'member1@gmail.com', '1234', 'member', NULL);

INSERT INTO tools (id, name, icon, status, location) VALUES
  ('TD-001', 'Pliers', '🔧', 'available', 'Locker A-12'),
  ('TD-002', 'Screwdriver', '🪛', 'available', 'Locker B-04'),
  ('TD-003', 'Hammer', '🔨', 'available', 'Locker A-00');
