create table public.cosmetics_profiles (
  id             bigint generated always as identity primary key,
  minecraft_uuid uuid unique,
  offline_uuid   uuid unique,
  cape_id        text,
  hat_id         text,
  updated_at     timestamptz not null default now(),
  constraint at_least_one_uuid
    check (minecraft_uuid is not null or offline_uuid is not null)
);

alter table public.cosmetics_profiles enable row level security;

create policy "cosmetics are publicly readable"
  on public.cosmetics_profiles
  for select
  to anon, authenticated
  using (true);
