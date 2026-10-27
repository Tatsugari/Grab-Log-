-- Optional: dedicated RFID column on logs (details already stores RFID if you skip this)
alter table logs add column if not exists card_uid text;

-- See who tapped what
-- select created_at, action, tool_name, user_name, details from logs order by created_at desc limit 20;
