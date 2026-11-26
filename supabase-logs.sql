-- Run in Supabase SQL Editor
create table if not exists logs (
  id uuid primary key default gen_random_uuid(),
  action text not null,
  tool_id text not null,
  tool_name text not null,
  user_name text not null,
  user_email text,
  details text,
  created_at timestamptz default now()
);

alter table logs enable row level security;
drop policy if exists "allow all logs" on logs;
create policy "allow all logs" on logs for all using (true) with check (true);

create index if not exists logs_created_at_idx on logs (created_at desc);
create index if not exists logs_tool_id_idx on logs (tool_id);
