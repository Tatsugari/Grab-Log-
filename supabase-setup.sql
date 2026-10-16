-- Run this in Supabase: SQL Editor → New query → Run

-- Operators table
create table if not exists operators (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  email text not null unique,
  pin text not null,
  created_at timestamptz default now()
);

-- Tools table
create table if not exists tools (
  id text primary key,
  name text not null,
  icon text default '🔧',
  status text not null default 'available',
  location text not null default '',
  logged_to text
);

-- Seed tools (matches your design)
insert into tools (id, name, icon, status, location, logged_to) values
  ('TD-001', 'Pliers', '🔧', 'available', 'Locker A-12', null),
  ('TD-002', 'Screwdriver', '🪛', 'inuse', 'Locker B-04', 'Alex'),
  ('TD-003', 'Hammer', '🔨', 'overdue', 'Locker A-03', 'Marcus Vance'),
  ('TD-004', 'Flat Screwdriver', '🪛', 'disposed', 'Disposal Bin', null)
on conflict (id) do nothing;

-- Allow access with the publishable/anon key
alter table operators enable row level security;
alter table tools enable row level security;

create policy "allow all operators" on operators for all using (true) with check (true);
create policy "allow all tools" on tools for all using (true) with check (true);
