-- Kompatibilnost s postojećim NexusOps financijskim tablicama.
-- Pokreni jednom prije demo seeda; ne briše niti mijenja postojeće zapise.

alter table loans add column if not exists organization_id uuid references organizations(id) on delete set null;
alter table loans add column if not exists loan_code varchar(80);
alter table loans add column if not exists lender_name varchar(200);
alter table loans add column if not exists loan_name varchar(200);
alter table loans add column if not exists reference_number varchar(100);
alter table loans add column if not exists currency char(3) not null default 'EUR';
alter table loans add column if not exists principal_amount numeric(14,2);
alter table loans add column if not exists interest_rate numeric(7,4) not null default 0;
alter table loans add column if not exists monthly_installment numeric(14,2);
alter table loans add column if not exists outstanding_balance numeric(14,2);
alter table loans add column if not exists starts_on date;
alter table loans add column if not exists matures_on date;
alter table loans add column if not exists status varchar(20) not null default 'active';
alter table loans add column if not exists notes text;
alter table loans add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_loans_tenant_reference_number on loans (tenant_id, reference_number);

alter table loan_installments add column if not exists tenant_id uuid references tenants(id) on delete cascade;
alter table loan_installments add column if not exists principal_amount numeric(14,2) not null default 0;
alter table loan_installments add column if not exists interest_amount numeric(14,2) not null default 0;
alter table loan_installments add column if not exists paid_on date;
alter table loan_installments add column if not exists status varchar(20) not null default 'planned';
alter table loan_installments add column if not exists financial_transaction_id uuid references financial_transactions(id) on delete set null;
alter table loan_installments add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_loan_installments_loan_due on loan_installments (loan_id, due_on);

alter table public_tenders add column if not exists organization_id uuid references organizations(id) on delete set null;
alter table public_tenders add column if not exists tender_number varchar(120);
alter table public_tenders add column if not exists title varchar(300);
alter table public_tenders add column if not exists contracting_authority varchar(250);
alter table public_tenders add column if not exists source_url text;
alter table public_tenders add column if not exists published_on date;
alter table public_tenders add column if not exists deadline_at timestamptz;
alter table public_tenders add column if not exists estimated_value numeric(14,2);
alter table public_tenders add column if not exists currency char(3) not null default 'EUR';
alter table public_tenders add column if not exists status varchar(30) not null default 'monitoring';
alter table public_tenders add column if not exists owner_name varchar(150);
alter table public_tenders add column if not exists notes text;
alter table public_tenders add column if not exists updated_at timestamptz not null default now();
create unique index if not exists ux_public_tenders_tenant_number on public_tenders (tenant_id, tender_number);

alter table public_tender_documents add column if not exists tenant_id uuid references tenants(id) on delete cascade;
alter table public_tender_documents add column if not exists document_name varchar(250);
alter table public_tender_documents add column if not exists document_url text;
alter table public_tender_documents add column if not exists document_type varchar(80);
