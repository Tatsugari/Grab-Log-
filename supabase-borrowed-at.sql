-- Run in Supabase SQL Editor
-- Tracks when a tool was borrowed so we can mark overdue after 9 hours (8AM–5PM shift)

alter table tools add column if not exists borrowed_at timestamptz;

-- Optional: clear any old inuse without timestamp
-- update tools set borrowed_at = now() - interval '10 hours' where status in ('inuse','overdue') and borrowed_at is null;
