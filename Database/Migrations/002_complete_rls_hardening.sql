do $$
declare
    table_name text;
begin
    foreach table_name in array array[
        'employee_presence_events',
        'loan_installments',
        'nexusops_release_metadata',
        'nexusops_schema_migrations',
        'public_tender_documents',
        'public_tenders'
    ] loop
        if to_regclass('public.' || table_name) is not null then
            execute format('revoke all on table public.%I from anon, authenticated', table_name);
            execute format('alter table public.%I enable row level security', table_name);
        end if;
    end loop;
end $$;
