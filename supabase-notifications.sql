-- Run in Supabase SQL Editor
create table if not exists notifications (
  id uuid primary key default gen_random_uuid(),
  type text not null,
  title text not null,
  message text not null,
  tool_id text,
  user_name text,
  for_role text not null default 'all',
  is_read boolean not null default false,
  created_at timestamptz default now()
);

alter table notifications enable row level security;
drop policy if exists "allow all notifications" on notifications;
create policy "allow all notifications" on notifications for all using (true) with check (true);

create index if not exists notifications_created_at_idx on notifications (created_at desc);
