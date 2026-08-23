-- Link an existing employee to their Supabase Auth account.
-- Replace both placeholder values once per employee.

update public.employees employee
set auth_user_id = auth_user.id,
    updated_at = now()
from auth.users auth_user
where employee.tenant_id = 'YOUR_TENANT_UUID'::uuid
  and lower(employee.email) = lower(auth_user.email)
  and lower(employee.email) = lower('TECHNICIAN_EMAIL@example.com');
