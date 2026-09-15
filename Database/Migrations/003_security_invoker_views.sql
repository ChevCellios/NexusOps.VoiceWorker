do $$
declare
    view_name text;
begin
    foreach view_name in array array[
        'v_trial_balance',
        'v_profit_and_loss',
        'v_balance_sheet',
        'v_loan_cashflow_forecast',
        'v_cashflow_forecast',
        'v_cashflow_rolling',
        'v_monthly_actuals',
        'v_budget_vs_actual',
        'work_order_report',
        'financial_account_balances'
    ]
    loop
        if to_regclass(format('public.%I', view_name)) is not null then
            execute format('alter view public.%I set (security_invoker = true)', view_name);
        end if;
    end loop;
end
$$;
