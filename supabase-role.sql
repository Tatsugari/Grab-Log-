-- Add role column to operators (run in SQL Editor)
alter table operators add column if not exists role text not null default 'member';

-- Optional: promote an existing user to admin
-- update operators set role = 'admin' where email = 'your-admin@email.com';
