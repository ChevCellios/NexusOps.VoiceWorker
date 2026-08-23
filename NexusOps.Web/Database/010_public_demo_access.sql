-- NexusOps public demo access v0.1
-- Run after 002_user_access.sql. It adds a restricted Demo role.

-- Compatibility with early installations of 002_user_access.sql.
alter table public.nexusops_user_roles add column if not exists email varchar(320);

do $$
declare constraint_name text;
begin
    for constraint_name in
        select conname
        from pg_constraint
        where conrelid = 'public.nexusops_user_roles'::regclass
          and contype = 'c'
          and pg_get_constraintdef(oid) ilike '%role%'
          and pg_get_constraintdef(oid) ilike '%Viewer%'
    loop
        execute format('alter table public.nexusops_user_roles drop constraint %I', constraint_name);
    end loop;
end $$;

alter table public.nexusops_user_roles
    add constraint nexusops_user_roles_role_check
    check (role in ('Demo', 'Viewer', 'Technician', 'Manager', 'Administrator'));

-- Create this user first in Supabase Dashboard -> Authentication -> Users:
-- e-mail: demo@nexusops.app
-- password: NexusOps!Demo26
-- Then replace YOUR_TENANT_UUID and run this mapping once:
-- insert into public.nexusops_user_roles (tenant_id, user_id, email, role)
-- select 'YOUR_TENANT_UUID'::uuid, id, email, 'Demo'
-- from auth.users
-- where email = 'demo@nexusops.app'
-- on conflict (tenant_id, user_id) do update
-- set email = excluded.email, role = excluded.role, is_active = true, updated_at = now();
