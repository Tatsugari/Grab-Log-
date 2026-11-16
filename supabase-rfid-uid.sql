-- Store RFID card UID on each member
alter table operators add column if not exists rfid_uid text;

-- Optional: one card = one person
create unique index if not exists operators_rfid_uid_uidx on operators (rfid_uid) where rfid_uid is not null;
