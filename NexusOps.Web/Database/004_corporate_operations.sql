-- NexusOps corporate operations v0.1
-- Poslovne jedinice koriste postojeću tablicu organizations.

create table if not exists employees (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    employee_code varchar(50) not null,
    full_name varchar(180) not null,
    email varchar(320),
    job_title varchar(160),
    department varchar(120),
    employment_status varchar(30) not null default 'active'
        check (employment_status in ('active', 'leave', 'inactive')),
    hired_on date,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, employee_code),
    unique (tenant_id, email)
);

-- Kompatibilnost s postojećom NexusOps tablicom employees.
alter table employees add column if not exists employee_code varchar(50);
alter table employees add column if not exists auth_user_id uuid;
alter table employees add column if not exists first_name varchar(120);
alter table employees add column if not exists last_name varchar(120);
alter table employees add column if not exists full_name varchar(180);
alter table employees add column if not exists email varchar(320);
alter table employees add column if not exists job_title varchar(160);
alter table employees add column if not exists department varchar(120);
alter table employees add column if not exists employment_status varchar(30) not null default 'active';
alter table employees add column if not exists hired_on date;
alter table employees add column if not exists organization_id uuid references organizations(id) on delete set null;
alter table employees add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_employees_tenant_employee_code on employees (tenant_id, employee_code);
create unique index if not exists ux_employees_tenant_auth_user on employees (tenant_id, auth_user_id) where auth_user_id is not null;

-- Trenutačni operativni status zaposlenika. Lokacija je opis radnog mjesta,
-- ne GPS praćenje; unosi se ručno ili iz odobrenog rasporeda.
create table if not exists employee_presence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    employee_id uuid not null references employees(id) on delete cascade,
    presence_status varchar(30) not null default 'working'
        check (presence_status in ('working', 'remote', 'on_site', 'focused', 'break', 'off_duty', 'on_leave', 'weekend')),
    work_location varchar(200),
    current_task varchar(300),
    do_not_disturb boolean not null default false,
    available_from timestamptz,
    status_started_at timestamptz not null default now(),
    updated_by_name varchar(150),
    updated_at timestamptz not null default now(),
    unique (employee_id)
);

create table if not exists employee_absences (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    employee_id uuid not null references employees(id) on delete cascade,
    absence_type varchar(30) not null
        check (absence_type in ('annual_leave', 'day_off', 'sick_leave', 'weekend', 'other')),
    starts_on date not null,
    ends_on date not null,
    note varchar(500),
    approval_status varchar(20) not null default 'approved'
        check (approval_status in ('requested', 'approved', 'cancelled')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    check (ends_on >= starts_on)
);

create table if not exists employee_presence_events (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    employee_id uuid not null references employees(id) on delete cascade,
    presence_status varchar(30) not null,
    work_location varchar(200),
    current_task varchar(300),
    do_not_disturb boolean not null default false,
    changed_by_name varchar(150),
    created_at timestamptz not null default now()
);

create index if not exists ix_employee_presence_tenant_status on employee_presence (tenant_id, presence_status);
create index if not exists ix_employee_absences_tenant_dates on employee_absences (tenant_id, starts_on, ends_on);

-- Pravilo rada po zaposleniku. "trust" ne zahtijeva evidentiranje svakog sata;
-- osoba i dalje ažurira prisutnost i napredak na zadacima kada je potrebno.
create table if not exists employee_work_policies (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    employee_id uuid not null references employees(id) on delete cascade,
    work_mode varchar(20) not null default 'clocked'
        check (work_mode in ('clocked', 'flexible', 'trust')),
    expected_daily_minutes integer check (expected_daily_minutes is null or expected_daily_minutes between 0 and 1440),
    track_time boolean not null default true,
    effective_from date not null default current_date,
    note varchar(500),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (employee_id)
);

-- Jedan otvoreni zapis predstavlja trenutačno radno vrijeme. Nema GPS-a:
-- mjesto rada i opis zadatka unose se samo kao operativni kontekst.
create table if not exists employee_time_entries (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    employee_id uuid not null references employees(id) on delete cascade,
    started_at timestamptz not null,
    ended_at timestamptz,
    work_location varchar(200),
    task_summary varchar(500),
    entry_source varchar(20) not null default 'manual'
        check (entry_source in ('login', 'logout', 'manual', 'manager')),
    note varchar(500),
    approved_by_name varchar(150),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    check (ended_at is null or ended_at >= started_at)
);

create unique index if not exists ux_employee_time_entries_open on employee_time_entries (employee_id) where ended_at is null;
create index if not exists ix_employee_time_entries_tenant_started on employee_time_entries (tenant_id, started_at desc);

create table if not exists warehouses (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    warehouse_code varchar(50) not null,
    name varchar(180) not null,
    location varchar(250),
    manager_name varchar(180),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, warehouse_code)
);

alter table warehouses add column if not exists warehouse_code varchar(50);
alter table warehouses add column if not exists name varchar(180);
alter table warehouses add column if not exists location varchar(250);
alter table warehouses add column if not exists manager_name varchar(180);
alter table warehouses add column if not exists is_active boolean not null default true;
alter table warehouses add column if not exists organization_id uuid references organizations(id) on delete set null;
alter table warehouses add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_warehouses_tenant_warehouse_code on warehouses (tenant_id, warehouse_code);

create table if not exists inventory_items (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    item_code varchar(80) not null,
    name varchar(220) not null,
    category varchar(120),
    unit_of_measure varchar(20) not null default 'kom',
    minimum_quantity numeric(14,3) not null default 0 check (minimum_quantity >= 0),
    unit_cost numeric(14,2) not null default 0 check (unit_cost >= 0),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, item_code)
);

alter table inventory_items add column if not exists item_code varchar(80);
alter table inventory_items add column if not exists name varchar(220);
alter table inventory_items add column if not exists category varchar(120);
alter table inventory_items add column if not exists unit_of_measure varchar(20) not null default 'kom';
alter table inventory_items add column if not exists minimum_quantity numeric(14,3) not null default 0;
alter table inventory_items add column if not exists unit_cost numeric(14,2) not null default 0;
alter table inventory_items add column if not exists is_active boolean not null default true;
alter table inventory_items add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_inventory_items_tenant_item_code on inventory_items (tenant_id, item_code);

create table if not exists inventory_stock (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    warehouse_id uuid not null references warehouses(id) on delete cascade,
    item_id uuid not null references inventory_items(id) on delete cascade,
    quantity numeric(14,3) not null default 0 check (quantity >= 0),
    reserved_quantity numeric(14,3) not null default 0 check (reserved_quantity >= 0),
    updated_at timestamptz not null default now(),
    unique (warehouse_id, item_id)
);

alter table inventory_stock add column if not exists reserved_quantity numeric(14,3) not null default 0;
alter table inventory_stock add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_inventory_stock_warehouse_item on inventory_stock (warehouse_id, item_id);

create table if not exists inventory_movements (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    warehouse_id uuid not null references warehouses(id) on delete restrict,
    item_id uuid not null references inventory_items(id) on delete restrict,
    work_order_id uuid references work_orders(id) on delete set null,
    movement_type varchar(30) not null
        check (movement_type in ('receipt', 'issue', 'transfer_in', 'transfer_out', 'adjustment')),
    quantity numeric(14,3) not null check (quantity > 0),
    occurred_at timestamptz not null default now(),
    note text,
    created_by_name varchar(150),
    created_at timestamptz not null default now()
);

create index if not exists ix_inventory_stock_tenant on inventory_stock (tenant_id, warehouse_id);
create index if not exists ix_inventory_movements_tenant_date on inventory_movements (tenant_id, occurred_at desc);

create table if not exists fleet_assets (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    asset_code varchar(80) not null,
    name varchar(200) not null,
    asset_type varchar(30) not null
        check (asset_type in ('vehicle', 'drone', 'robot', 'machine')),
    manufacturer varchar(150),
    model varchar(150),
    registration_number varchar(80),
    serial_number varchar(150),
    location varchar(200),
    operational_status varchar(30) not null default 'operational'
        check (operational_status in ('operational', 'maintenance', 'out_of_service', 'retired')),
    acquired_on date,
    notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, asset_code)
);

alter table fleet_assets add column if not exists asset_code varchar(80);
alter table fleet_assets add column if not exists name varchar(200);
alter table fleet_assets add column if not exists asset_type varchar(30);
alter table fleet_assets add column if not exists manufacturer varchar(150);
alter table fleet_assets add column if not exists model varchar(150);
alter table fleet_assets add column if not exists registration_number varchar(80);
alter table fleet_assets add column if not exists serial_number varchar(150);
alter table fleet_assets add column if not exists location varchar(200);
alter table fleet_assets add column if not exists operational_status varchar(30) not null default 'operational';
alter table fleet_assets add column if not exists acquired_on date;
alter table fleet_assets add column if not exists notes text;
alter table fleet_assets add column if not exists organization_id uuid references organizations(id) on delete set null;
alter table fleet_assets add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_fleet_assets_tenant_asset_code on fleet_assets (tenant_id, asset_code);

create index if not exists ix_fleet_assets_tenant_type on fleet_assets (tenant_id, asset_type, operational_status);
