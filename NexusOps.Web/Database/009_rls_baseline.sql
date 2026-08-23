-- NexusOps RLS baseline v0.1
-- Run in Supabase SQL Editor after all current NexusOps schema scripts.
-- This blocks direct Data API/browser access to NexusOps business data.
-- Railway accesses the database server-side and keeps tenant/role checks in the app.

do $$
declare table_name text;
begin
  foreach table_name in array array[
    'tenants','organizations','assets','work_orders','work_order_events',
    'nexusops_user_roles','financial_accounts','financial_categories',
    'financial_transactions','loans','tenders','tender_documents','employees',
    'employee_presence','employee_absences','employee_work_policies',
    'employee_time_entries','warehouses','inventory_items','inventory_stock',
    'inventory_movements','fleet_assets','customer_orders','work_order_labor_entries'
  ] loop
    if to_regclass('public.' || table_name) is not null then
      execute format('revoke all on table public.%I from anon, authenticated', table_name);
      execute format('alter table public.%I enable row level security', table_name);
    end if;
  end loop;
end $$;

-- Verification: every listed table should report rowsecurity = true.
select relname as table_name, relrowsecurity as rls_enabled
from pg_class
where relnamespace = 'public'::regnamespace
  and relname in (
    'tenants','organizations','assets','work_orders','work_order_events',
    'nexusops_user_roles','financial_accounts','financial_categories',
    'financial_transactions','loans','tenders','tender_documents','employees',
    'employee_presence','employee_absences','employee_work_policies',
    'employee_time_entries','warehouses','inventory_items','inventory_stock',
    'inventory_movements','fleet_assets','customer_orders','work_order_labor_entries'
  )
order by relname;
