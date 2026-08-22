-- NexusOps customer orders and automatic work-order creation.
-- Run once in Supabase SQL Editor after 001_operations_schema.sql.

create table if not exists customer_orders (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    order_number varchar(80) not null,
    customer_name varchar(200) not null,
    customer_reference varchar(120),
    description text not null,
    priority varchar(20) not null default 'normal'
        check (priority in ('low', 'normal', 'high', 'critical')),
    required_by timestamptz,
    status varchar(30) not null default 'work_order_created'
        check (status in ('received', 'work_order_created', 'completed', 'cancelled')),
    work_order_id uuid references work_orders(id) on delete set null,
    assigned_to_name varchar(150),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, order_number)
);

create index if not exists ix_customer_orders_tenant_created
    on customer_orders (tenant_id, created_at desc);
