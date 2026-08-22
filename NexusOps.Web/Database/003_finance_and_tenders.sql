-- NexusOps finance and public procurement v0.1
-- Preduvjet: postoje tablice tenants(id), organizations(id), assets(id) i work_orders(id).

create table if not exists financial_accounts (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    name varchar(160) not null,
    account_type varchar(30) not null default 'bank'
        check (account_type in ('bank', 'cash', 'card', 'reserve')),
    currency char(3) not null default 'EUR',
    opening_balance numeric(14,2) not null default 0,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, name)
);

create table if not exists financial_categories (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    name varchar(160) not null,
    category_type varchar(20) not null
        check (category_type in ('income', 'expense')),
    color varchar(20),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    unique (tenant_id, name, category_type)
);

create table if not exists financial_transactions (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    account_id uuid not null references financial_accounts(id) on delete restrict,
    category_id uuid references financial_categories(id) on delete set null,
    work_order_id uuid references work_orders(id) on delete set null,
    asset_id uuid references assets(id) on delete set null,
    transaction_type varchar(20) not null
        check (transaction_type in ('income', 'expense')),
    status varchar(20) not null default 'recorded'
        check (status in ('planned', 'recorded', 'paid', 'cancelled')),
    amount numeric(14,2) not null check (amount > 0),
    currency char(3) not null default 'EUR',
    occurred_on date not null default current_date,
    counterparty varchar(200),
    reference_number varchar(100),
    description text,
    created_by_name varchar(150),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists ix_financial_transactions_tenant_date on financial_transactions (tenant_id, occurred_on desc);
create index if not exists ix_financial_transactions_work_order on financial_transactions (work_order_id) where work_order_id is not null;
create index if not exists ix_financial_transactions_asset on financial_transactions (asset_id) where asset_id is not null;

create table if not exists loans (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    lender_name varchar(200) not null,
    loan_name varchar(200) not null,
    reference_number varchar(100),
    currency char(3) not null default 'EUR',
    principal_amount numeric(14,2) not null check (principal_amount > 0),
    interest_rate numeric(7,4) not null default 0 check (interest_rate >= 0),
    monthly_installment numeric(14,2),
    outstanding_balance numeric(14,2) not null check (outstanding_balance >= 0),
    starts_on date,
    matures_on date,
    status varchar(20) not null default 'active'
        check (status in ('draft', 'active', 'closed', 'cancelled')),
    notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists loan_installments (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    loan_id uuid not null references loans(id) on delete cascade,
    due_on date not null,
    principal_amount numeric(14,2) not null default 0 check (principal_amount >= 0),
    interest_amount numeric(14,2) not null default 0 check (interest_amount >= 0),
    paid_on date,
    status varchar(20) not null default 'planned'
        check (status in ('planned', 'paid', 'overdue', 'cancelled')),
    financial_transaction_id uuid references financial_transactions(id) on delete set null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (loan_id, due_on)
);

create index if not exists ix_loan_installments_due on loan_installments (tenant_id, due_on) where status in ('planned', 'overdue');

create table if not exists public_tenders (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    organization_id uuid references organizations(id) on delete set null,
    tender_number varchar(120),
    title varchar(300) not null,
    contracting_authority varchar(250),
    source_url text,
    published_on date,
    deadline_at timestamptz,
    estimated_value numeric(14,2),
    currency char(3) not null default 'EUR',
    status varchar(30) not null default 'monitoring'
        check (status in ('monitoring', 'preparing', 'submitted', 'won', 'lost', 'cancelled')),
    owner_name varchar(150),
    notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (tenant_id, tender_number)
);

create index if not exists ix_public_tenders_tenant_deadline on public_tenders (tenant_id, deadline_at) where status in ('monitoring', 'preparing', 'submitted');

create table if not exists public_tender_documents (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenants(id) on delete cascade,
    tender_id uuid not null references public_tenders(id) on delete cascade,
    document_name varchar(250) not null,
    document_url text,
    document_type varchar(80),
    created_at timestamptz not null default now()
);

-- Bilanca po računu: prihod povećava, trošak smanjuje raspoloživi iznos.
create or replace view financial_account_balances as
select
    accounts.tenant_id,
    accounts.organization_id,
    accounts.id as account_id,
    accounts.name as account_name,
    accounts.currency,
    accounts.opening_balance + coalesce(sum(case
        when transactions.transaction_type = 'income' then transactions.amount
        when transactions.transaction_type = 'expense' then -transactions.amount
        else 0 end) filter (where transactions.status in ('recorded', 'paid')), 0) as current_balance
from financial_accounts accounts
left join financial_transactions transactions on transactions.account_id = accounts.id
group by accounts.tenant_id, accounts.organization_id, accounts.id, accounts.name, accounts.currency, accounts.opening_balance;
