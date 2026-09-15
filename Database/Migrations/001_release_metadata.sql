create table if not exists nexusops_release_metadata (
    singleton boolean primary key default true check (singleton),
    application_version varchar(100) not null,
    deployed_at timestamptz not null default now()
);
