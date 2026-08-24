-- NexusOps employee login linking v0.1
-- Run once after creating Supabase Auth accounts for your employees.
-- Replace YOUR_TENANT_UUID with the same value used in Railway NexusOps__TenantId.
-- This safely links every employee whose e-mail matches a Supabase Auth user.

update public.employees employee
set auth_user_id = auth_user.id,
    updated_at = now()
from auth.users auth_user
where employee.tenant_id = 'YOUR_TENANT_UUID'::uuid
  and employee.email is not null
  and lower(employee.email) = lower(auth_user.email)
  and (employee.auth_user_id is null or employee.auth_user_id = auth_user.id);

-- Verification: this should show the employees who can use "Moji radni nalozi".
select
    employee.full_name,
    employee.email,
    employee.job_title,
    employee.auth_user_id,
    case when employee.auth_user_id is null then 'Nije povezan' else 'Povezan' end as status
from public.employees employee
where employee.tenant_id = 'YOUR_TENANT_UUID'::uuid
order by employee.full_name;
