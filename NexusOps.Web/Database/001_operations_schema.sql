-- NexusOps operational core v0.1
-- Preduvjet: postoje tablice tenants(id) i organizations(id).

create table if not exists assets (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    asset_code varchar(80) not null,
    name varchar(200) not null,
    asset_type varchar(100),
    location varchar(200),
    status varchar(30) not null default 'operational'
        check (status in ('operational', 'attention_required', 'out_of_service', 'retired')),
    manufacturer varchar(150),
    model varchar(150),
    serial_number varchar(150),
    installed_at date,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, asset_code)
);

create index if not exists ix_assets_tenant_organization on assets (tenant_id, organization_id);
create index if not exists ix_assets_tenant_status on assets (tenant_id, status);

create table if not exists work_orders (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    asset_id uuid references assets(id) on delete set null,
    work_order_number varchar(80) not null,
    title varchar(250) not null,
    description text,
    priority varchar(20) not null default 'normal'
        check (priority in ('low', 'normal', 'high', 'critical')),
    status varchar(30) not null default 'new'
        check (status in ('new', 'assigned', 'in_progress', 'waiting_parts', 'completed', 'cancelled')),
    assigned_to_name varchar(150),
    due_at timestamptz,
    completed_at timestamptz,
    source varchar(30) not null default 'web'
        check (source in ('web', 'voice', 'api', 'import')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, work_order_number)
);

create index if not exists ix_work_orders_tenant_status on work_orders (tenant_id, status, created_at desc);
create index if not exists ix_work_orders_tenant_asset on work_orders (tenant_id, asset_id);
create index if not exists ix_work_orders_due_at on work_orders (tenant_id, due_at) where status not in ('completed', 'cancelled');

create table if not exists work_order_events (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    work_order_id uuid not null references work_orders(id) on delete cascade,
    event_type varchar(50) not null,
    message text,
    actor_name varchar(150),
    created_at timestamptz not null default now()
);

create index if not exists ix_work_order_events_order on work_order_events (work_order_id, created_at);

-- Primjer izvještajnog pogleda za kasniju upotrebu u NexusOps.Web.
create or replace view work_order_report as
select
    orders.tenant_id,
    orders.organization_id,
    orders.id,
    orders.work_order_number,
    orders.title,
    orders.priority,
    orders.status,
    orders.created_at,
    orders.due_at,
    orders.completed_at,
    assets.asset_code,
    assets.name as asset_name,
    assets.location as asset_location
from work_orders orders
left join assets on assets.id = orders.asset_id;
