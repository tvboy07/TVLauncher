-- Endurecimiento de permisos de la tabla de cosméticos.
--
-- La política RLS ya limita las escrituras al service_role (no hay políticas de
-- INSERT/UPDATE/DELETE), pero los privilegios por defecto de Supabase pueden
-- conceder DML a anon/authenticated. Se revocan explícitamente para que la única
-- superficie pública sea la lectura.

revoke all on table public.cosmetics_profiles from anon, authenticated;
grant select on table public.cosmetics_profiles to anon, authenticated;
