create table if not exists work_order_labor_entries (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenants(id) on delete cascade,
 work_order_id uuid not null references work_orders(id) on delete cascade, employee_id uuid references employees(id) on delete set null,
 employee_name varchar(200) not null, started_at timestamptz not null, ended_at timestamptz not null,
 hourly_rate numeric(14,2) not null default 0 check(hourly_rate>=0), note varchar(500), created_at timestamptz not null default now(),
 check(ended_at>started_at)
);
create index if not exists ix_work_order_labor_entries_order on work_order_labor_entries(work_order_id,started_at desc);
