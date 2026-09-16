create table if not exists voice_call_queue (
    voice_call_session_id uuid primary key references voice_call_sessions(id) on delete cascade,
    tenant_id uuid not null,
    agent_task_id uuid not null,
    status varchar(20) not null default 'pending'
        check (status in ('pending', 'processing', 'completed', 'dead_letter')),
    attempts integer not null default 0 check (attempts >= 0),
    available_at timestamptz not null default now(),
    locked_at timestamptz,
    lock_id uuid,
    last_error text,
    completed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists ix_voice_call_queue_claim
    on voice_call_queue (tenant_id, status, available_at, created_at);

revoke all on table voice_call_queue from anon, authenticated;
alter table voice_call_queue enable row level security;

insert into voice_call_queue (voice_call_session_id, tenant_id, agent_task_id)
select id, tenant_id, agent_task_id
from voice_call_sessions
where status = 'queued'
on conflict (voice_call_session_id) do nothing;
