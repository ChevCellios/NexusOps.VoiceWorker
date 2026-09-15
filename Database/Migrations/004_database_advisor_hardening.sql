drop index if exists public.idx_action_tasks_workflow;
drop index if exists public.idx_agent_tasks_action;
drop index if exists public.idx_voice_messages_session;
drop index if exists public.ux_fleet_assets_tenant_asset_code;
drop index if exists public.ux_inventory_items_tenant_item_code;
drop index if exists public.ux_inventory_stock_warehouse_item;
drop index if exists public.ux_loan_installments_loan_due;
drop index if exists public.ux_public_tenders_tenant_number;
drop index if exists public.ux_warehouses_tenant_warehouse_code;

do $$
declare
    function_signature text;
begin
    for function_signature in
        select p.oid::regprocedure::text
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.prosecdef
    loop
        execute format(
            'revoke execute on function %s from public, anon, authenticated',
            function_signature
        );
        execute format(
            'alter function %s set search_path = pg_catalog, public',
            function_signature
        );
    end loop;
end
$$;

grant execute on function public.has_tenant_role(uuid, text[]) to authenticated;
grant execute on function public.is_tenant_member(uuid) to authenticated;
