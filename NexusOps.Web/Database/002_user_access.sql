-- NexusOps user authorization v0.1
-- Supabase Auth owns credentials in auth.users. This table maps those users to a NexusOps tenant and role.

create table if not exists nexusops_user_roles (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    user_id uuid not null,
    role varchar(30) not null check (role in ('Viewer', 'Technician', 'Manager', 'Administrator')),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, user_id)
);

create index if not exists ix_nexusops_user_roles_user on nexusops_user_roles (user_id, is_active);

-- After creating a user in Supabase Authentication, grant its user UUID access:
-- insert into nexusops_user_roles (tenant_id, user_id, role)
-- values ('YOUR_TENANT_UUID', 'SUPABASE_AUTH_USER_UUID', 'Administrator');
