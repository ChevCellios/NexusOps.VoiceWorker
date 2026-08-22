-- NexusOps demo dataset: Adria Dynamics d.o.o.
-- Pokreni nakon 001, 003 i 004. Skripta koristi prvi postojeći tenant.
-- Sigurna je za ponavljanje: ne duplicira zapise s istim poslovnim kodom.

create temporary table nexusops_demo_context as
select id as tenant_id from tenants order by id limit 1;

do $$
begin
    if not exists (select 1 from nexusops_demo_context) then
        raise exception 'Nije pronađen tenant. Prvo stvori NexusOps tenant.';
    end if;
end $$;

insert into organizations (tenant_id, name)
select context.tenant_id, unit.name
from nexusops_demo_context context
cross join (values
    ('Adria Dynamics d.o.o. — Zagreb Operacije'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika'),
    ('Adria Dynamics d.o.o. — Osijek Robotika')
) as unit(name)
where not exists (
    select 1 from organizations existing
    where existing.tenant_id = context.tenant_id and existing.name = unit.name
);

insert into employees (tenant_id, organization_id, employee_code, first_name, last_name, full_name, email, job_title, department, hired_on)
select context.tenant_id, organization.id, employee.code, employee.first_name, employee.last_name, employee.full_name, employee.email, employee.job_title, employee.department, employee.hired_on
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics d.o.o. — Zagreb Operacije','AD-001','Ivana','Kovačević','Ivana Kovačević','ivana.kovacevic@adria-demo.hr','Direktorica operacija','Uprava',date '2021-03-01'),
    ('Adria Dynamics d.o.o. — Zagreb Operacije','AD-002','Luka','Marić','Luka Marić','luka.maric@adria-demo.hr','Voditelj održavanja','Održavanje',date '2022-06-15'),
    ('Adria Dynamics d.o.o. — Zagreb Operacije','AD-003','Petra','Novak','Petra Novak','petra.novak@adria-demo.hr','Financijska analitičarka','Financije',date '2023-01-10'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','AD-101','Marko','Horvat','Marko Horvat','marko.horvat@adria-demo.hr','Voditelj logistike','Logistika',date '2020-09-01'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','AD-102','Ana','Babić','Ana Babić','ana.babic@adria-demo.hr','Skladištarka','Skladište',date '2023-04-01'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','AD-103','Dino','Barišić','Dino Barišić','dino.barisic@adria-demo.hr','Vozač','Transport',date '2022-11-20'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','AD-201','Tomislav','Radić','Tomislav Radić','tomislav.radic@adria-demo.hr','Voditelj automatizacije','Robotika',date '2021-08-01'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','AD-202','Mia','Jurić','Mia Jurić','mia.juric@adria-demo.hr','Robotika inženjerka','Robotika',date '2024-02-01'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','AD-203','Karlo','Šarić','Karlo Šarić','karlo.saric@adria-demo.hr','Operater drona','Terenske usluge',date '2023-06-10')
) as employee(organization_name, code, first_name, last_name, full_name, email, job_title, department, hired_on)
on organization.name = employee.organization_name
on conflict (tenant_id, employee_code) do update set
    first_name = excluded.first_name, last_name = excluded.last_name, full_name = excluded.full_name, email = excluded.email, job_title = excluded.job_title, department = excluded.department, updated_at = now();

insert into employee_presence (tenant_id, employee_id, presence_status, work_location, current_task, do_not_disturb, available_from, updated_by_name)
select context.tenant_id, employee.id, presence.status, presence.location, presence.task, presence.do_not_disturb, presence.available_from, 'NexusOps demo seed'
from nexusops_demo_context context
join employees employee on employee.tenant_id = context.tenant_id
join (values
    ('AD-001','focused','Zagreb — uprava','Pregled ponude za preventivno održavanje',true,current_timestamp + interval '50 minutes'),
    ('AD-002','on_site','Pogon Zagreb A','Dijagnostika CNC obradnog centra',false,null),
    ('AD-003','working','Zagreb — ured','Mjesečni pregled prihoda i rashoda',false,null),
    ('AD-101','on_site','Rijeka — skladište','Koordinacija jutarnje isporuke',false,null),
    ('AD-102','working','Rijeka — skladište','Zaprimanje rezervnih dijelova',false,null),
    ('AD-103','remote','Terenski transport','Dostava dijelova prema Zagrebu',false,null),
    ('AD-201','focused','Osijek — laboratorij','Kalibracija mobilnog robota',true,current_timestamp + interval '90 minutes'),
    ('AD-202','on_leave',null,'Godišnji odmor',true,current_timestamp + interval '3 days'),
    ('AD-203','weekend',null,'Slobodan vikend',false,date_trunc('week', current_date)::timestamp + interval '7 days')
) as presence(employee_code, status, location, task, do_not_disturb, available_from)
on employee.employee_code = presence.employee_code
on conflict (employee_id) do update set
    presence_status = excluded.presence_status, work_location = excluded.work_location, current_task = excluded.current_task,
    do_not_disturb = excluded.do_not_disturb, available_from = excluded.available_from, status_started_at = now(), updated_at = now(), updated_by_name = excluded.updated_by_name;

insert into employee_absences (tenant_id, employee_id, absence_type, starts_on, ends_on, note, approval_status)
select context.tenant_id, employee.id, absence.absence_type, absence.starts_on, absence.ends_on, absence.note, 'approved'
from nexusops_demo_context context
join employees employee on employee.tenant_id = context.tenant_id
join (values
    ('AD-202','annual_leave',current_date,current_date + 4,'Planirani godišnji odmor'),
    ('AD-203','weekend',date_trunc('week', current_date)::date + 5,date_trunc('week', current_date)::date + 6,'Slobodan vikend')
) as absence(employee_code, absence_type, starts_on, ends_on, note)
on employee.employee_code = absence.employee_code
where not exists (
    select 1 from employee_absences existing
    where existing.employee_id = employee.id and existing.absence_type = absence.absence_type
      and existing.starts_on = absence.starts_on and existing.ends_on = absence.ends_on
);

insert into employee_work_policies (tenant_id, employee_id, work_mode, expected_daily_minutes, track_time, note)
select context.tenant_id, employee.id, policy.work_mode, policy.expected_minutes, policy.track_time, policy.note
from nexusops_demo_context context
join employees employee on employee.tenant_id = context.tenant_id
join (values
    ('AD-001','trust',null,false,'Upravljanje prema dogovorenim ciljevima i zadacima.'),
    ('AD-002','clocked',480,true,'Rad u pogonu; početak i završetak smjene.'),
    ('AD-003','flexible',480,true,'Fleksibilan početak rada uz dnevnu evidenciju.'),
    ('AD-101','clocked',480,true,'Logistička smjena.'),
    ('AD-102','clocked',480,true,'Skladišna smjena.'),
    ('AD-103','flexible',480,true,'Terenski rad i samostalna organizacija rute.'),
    ('AD-201','trust',null,false,'Razvojni rad prema rezultatima i planu sprinta.'),
    ('AD-202','flexible',480,true,'Fleksibilan raspored uz evidentirane dane rada.'),
    ('AD-203','flexible',480,true,'Terenske aktivnosti prema rasporedu letova.')
) as policy(employee_code, work_mode, expected_minutes, track_time, note)
on employee.employee_code = policy.employee_code
on conflict (employee_id) do update set work_mode = excluded.work_mode, expected_daily_minutes = excluded.expected_daily_minutes, track_time = excluded.track_time, note = excluded.note, updated_at = now();

insert into employee_time_entries (tenant_id, employee_id, started_at, ended_at, work_location, task_summary, entry_source, note)
select context.tenant_id, employee.id, entry.started_at, entry.ended_at, entry.location, entry.task, entry.source, entry.note
from nexusops_demo_context context
join employees employee on employee.tenant_id = context.tenant_id
join (values
    ('AD-001',current_timestamp - interval '4 hours',null::timestamptz,'Zagreb — uprava','Pregled ponude za preventivno održavanje','login','Demo otvoreni radni dan'),
    ('AD-002',current_timestamp - interval '2 hours 30 minutes',null::timestamptz,'Pogon Zagreb A','Dijagnostika CNC obradnog centra','login','Demo otvoreni radni dan'),
    ('AD-003',current_timestamp - interval '3 hours',null::timestamptz,'Zagreb — ured','Mjesečni pregled prihoda i rashoda','manual','Demo otvoreni radni dan'),
    ('AD-101',current_timestamp - interval '5 hours',null::timestamptz,'Rijeka — skladište','Koordinacija jutarnje isporuke','login','Demo otvoreni radni dan'),
    ('AD-102',current_timestamp - interval '5 hours',null::timestamptz,'Rijeka — skladište','Zaprimanje rezervnih dijelova','login','Demo otvoreni radni dan'),
    ('AD-103',current_timestamp - interval '1 hour 40 minutes',null::timestamptz,'Terenski transport','Dostava dijelova prema Zagrebu','manual','Demo otvoreni radni dan')
) as entry(employee_code, started_at, ended_at, location, task, source, note)
on employee.employee_code = entry.employee_code
where not exists (select 1 from employee_time_entries existing where existing.employee_id = employee.id and existing.ended_at is null);

insert into assets (tenant_id, organization_id, asset_code, name, asset_type, location, status, manufacturer, model, serial_number, installed_at)
select context.tenant_id, organization.id, asset.code, asset.name, asset.asset_type, asset.location, asset.status, asset.manufacturer, asset.model, asset.serial_number, asset.installed_on
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics d.o.o. — Zagreb Operacije','CNC-AD-01','CNC obradni centar','Proizvodni stroj','Pogon Zagreb A','operational','DMG Mori','CMX 600 V','DMG-CMX-600-ADR',date '2022-05-16'),
    ('Adria Dynamics d.o.o. — Zagreb Operacije','CMP-AD-01','Industrijski kompresor','Energetska oprema','Pogon Zagreb B','operational','Atlas Copco','GA 18','AC-GA18-ADR',date '2022-07-01'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','RBT-AD-01','Mobilni inspekcijski robot','Robotika','Laboratorij Osijek','operational','Boston Dynamics','Spot','BD-SPOT-ADR-01',date '2024-02-05')
) as asset(organization_name, code, name, asset_type, location, status, manufacturer, model, serial_number, installed_on)
on organization.name = asset.organization_name
on conflict (tenant_id, asset_code) do update set
    name = excluded.name, location = excluded.location, status = excluded.status, manufacturer = excluded.manufacturer, model = excluded.model, serial_number = excluded.serial_number, updated_at = now();

insert into warehouses (tenant_id, organization_id, warehouse_code, name, location, manager_name)
select context.tenant_id, organization.id, warehouse.code, warehouse.name, warehouse.location, warehouse.manager
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics d.o.o. — Zagreb Operacije','WH-ZG-01','Centralno skladište Zagreb','Radnička cesta 80, Zagreb','Luka Marić'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','WH-RI-01','Logističko skladište Rijeka','Industrijska zona Kukuljanovo, Rijeka','Marko Horvat'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','WH-OS-01','Tehničko skladište Osijek','Vukovarska 210, Osijek','Tomislav Radić')
) as warehouse(organization_name, code, name, location, manager)
on organization.name = warehouse.organization_name
on conflict (tenant_id, warehouse_code) do update set
    name = excluded.name, location = excluded.location, manager_name = excluded.manager_name, updated_at = now();

insert into inventory_items (tenant_id, item_code, name, category, unit_of_measure, minimum_quantity, unit_cost)
select context.tenant_id, item.code, item.name, item.category, item.unit, item.minimum_quantity, item.unit_cost
from nexusops_demo_context context
cross join (values
    ('INV-001','Industrijski filter F-400','Održavanje','kom',12,18.50),
    ('INV-002','Ležaj 6204-2RS','Rezervni dijelovi','kom',20,8.90),
    ('INV-003','Hidraulično ulje HLP 46','Potrošni materijal','l',100,4.20),
    ('INV-004','Senzor blizine M18','Automatizacija','kom',10,34.00),
    ('INV-005','Propeler set za dron','Dronovi','set',6,42.00),
    ('INV-006','Baterija LiPo 6S','Dronovi','kom',4,180.00),
    ('INV-007','Zaštitne rukavice','Zaštitna oprema','par',30,6.50),
    ('INV-008','Transportna kutija 60x40','Ambalaža','kom',25,12.00)
) as item(code, name, category, unit, minimum_quantity, unit_cost)
on conflict (tenant_id, item_code) do update set
    name = excluded.name, category = excluded.category, minimum_quantity = excluded.minimum_quantity, unit_cost = excluded.unit_cost, updated_at = now();

insert into inventory_stock (tenant_id, warehouse_id, item_id, quantity, reserved_quantity)
select context.tenant_id, warehouse.id, item.id, stock.quantity, stock.reserved
from nexusops_demo_context context
join warehouses warehouse on warehouse.tenant_id = context.tenant_id
join inventory_items item on item.tenant_id = context.tenant_id
join (values
    ('WH-ZG-01','INV-001',36,4), ('WH-ZG-01','INV-002',54,8), ('WH-ZG-01','INV-003',220,20),
    ('WH-RI-01','INV-007',80,10), ('WH-RI-01','INV-008',140,25),
    ('WH-OS-01','INV-004',28,5), ('WH-OS-01','INV-005',14,2), ('WH-OS-01','INV-006',9,1)
) as stock(warehouse_code, item_code, quantity, reserved)
on warehouse.warehouse_code = stock.warehouse_code and item.item_code = stock.item_code
on conflict (warehouse_id, item_id) do update set quantity = excluded.quantity, reserved_quantity = excluded.reserved_quantity, updated_at = now();

insert into fleet_assets (tenant_id, organization_id, asset_code, name, asset_type, manufacturer, model, registration_number, serial_number, location, operational_status, acquired_on, notes)
select context.tenant_id, organization.id, fleet.code, fleet.name, fleet.asset_type, fleet.manufacturer, fleet.model, fleet.registration, fleet.serial, fleet.location, fleet.status, fleet.acquired_on, fleet.notes
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics d.o.o. — Rijeka Logistika','FLT-001','Dostavno vozilo 01','vehicle','Iveco','Daily 35S','RI-AD-101','ZCFC35A0001234567','Rijeka','operational',date '2023-03-14','Hladni transport rezervnih dijelova'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','FLT-002','Električni viličar','vehicle','Toyota','Traigo 48',null,'TYT-TR48-2023-09','Rijeka','maintenance',date '2023-09-02','Planirani servis baterije'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','DRN-001','Inspekcijski dron 01','drone','DJI','Matrice 350 RTK',null,'DJI-M350-ADR-001','Osijek','operational',date '2024-01-20','Termalni pregled postrojenja'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','DRN-002','Kartografski dron','drone','DJI','Mavic 3 Enterprise',null,'DJI-M3E-ADR-002','Osijek','operational',date '2024-04-11','Fotogrametrija i inventura'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','RBT-001','Mobilni inspekcijski robot','robot','Boston Dynamics','Spot',null,'BD-SPOT-ADR-01','Osijek','operational',date '2024-02-05','Vizualni pregled teško dostupnih zona'),
    ('Adria Dynamics d.o.o. — Zagreb Operacije','MCH-001','CNC obradni centar','machine','DMG Mori','CMX 600 V',null,'DMG-CMX-600-ADR','Zagreb','operational',date '2022-05-16','Glavna proizvodna linija'),
    ('Adria Dynamics d.o.o. — Zagreb Operacije','MCH-002','Industrijski kompresor','machine','Atlas Copco','GA 18',null,'AC-GA18-ADR','Zagreb','operational',date '2022-07-01','Pneumatska mreža pogona')
) as fleet(organization_name, code, name, asset_type, manufacturer, model, registration, serial, location, status, acquired_on, notes)
on organization.name = fleet.organization_name
on conflict (tenant_id, asset_code) do update set
    name = excluded.name, operational_status = excluded.operational_status, location = excluded.location, notes = excluded.notes, updated_at = now();

insert into financial_accounts (tenant_id, name, account_type, opening_balance)
select context.tenant_id, account.name, account.account_type, account.opening_balance
from nexusops_demo_context context
cross join (values
    ('Adria Dynamics — Glavni račun','bank',24500.00),
    ('Adria Dynamics — Operativna blagajna','cash',1200.00),
    ('Adria Dynamics — Rezerva za razvoj','reserve',18000.00)
) as account(name, account_type, opening_balance)
on conflict (tenant_id, name) do update set opening_balance = excluded.opening_balance, updated_at = now();

insert into financial_categories (tenant_id, name, category_type, color)
select context.tenant_id, category.name, category.category_type, category.color
from nexusops_demo_context context
cross join (values
    ('Usluge održavanja','income','#57dab8'), ('Prodaja logističkih usluga','income','#57dab8'),
    ('Rezervni dijelovi','expense','#ffb4c2'), ('Gorivo i transport','expense','#ffb4c2'), ('Plaće i doprinosi','expense','#ffb4c2'), ('Razvoj robotike','expense','#ffb4c2')
) as category(name, category_type, color)
on conflict (tenant_id, name, category_type) do update set color = excluded.color;

-- Kontni plan koristi postojeću tablicu accounts. Kredit je obveza s kreditnim saldom.
insert into accounts (tenant_id, organization_id, code, name, account_type, normal_balance)
select context.tenant_id, organization.id, '2300', 'Kredit za automatizaciju', 'liability', 'credit'
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id and organization.name = 'Adria Dynamics d.o.o. — Osijek Robotika'
on conflict (organization_id, code) do update set name = excluded.name, is_active = true;

insert into financial_transactions (tenant_id, account_id, category_id, transaction_type, status, amount, occurred_on, counterparty, reference_number, description, created_by_name)
select context.tenant_id, account.id, category.id, entry.transaction_type, 'recorded', entry.amount, entry.occurred_on, entry.counterparty, entry.reference_number, entry.description, 'NexusOps demo seed'
from nexusops_demo_context context
join financial_accounts account on account.tenant_id = context.tenant_id
join financial_categories category on category.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics — Glavni račun','Usluge održavanja','income',8700.00,current_date - 12,'Industrija Sjever d.o.o.','INV-2026-041','Mjesečno održavanje proizvodne linije'),
    ('Adria Dynamics — Glavni račun','Prodaja logističkih usluga','income',4250.00,current_date - 7,'Kvarner Trade d.o.o.','INV-2026-042','Distribucija robe'),
    ('Adria Dynamics — Glavni račun','Rezervni dijelovi','expense',1840.50,current_date - 9,'Tehno servis d.o.o.','UL-2026-119','Nabava filtera i ležajeva'),
    ('Adria Dynamics — Operativna blagajna','Gorivo i transport','expense',268.40,current_date - 4,'Petrol d.o.o.','GOR-2026-088','Gorivo dostavnog vozila'),
    ('Adria Dynamics — Glavni račun','Plaće i doprinosi','expense',6120.00,current_date - 2,'Obračun plaća','PL-2026-08','Plaće za demonstracijski tim'),
    ('Adria Dynamics — Rezerva za razvoj','Razvoj robotike','expense',2200.00,current_date - 1,'RoboLab d.o.o.','RND-2026-021','Nadogradnja senzorskog paketa')
) as entry(account_name, category_name, transaction_type, amount, occurred_on, counterparty, reference_number, description)
on account.name = entry.account_name and category.name = entry.category_name and category.category_type = entry.transaction_type
where not exists (
    select 1 from financial_transactions existing
    where existing.tenant_id = context.tenant_id and existing.reference_number = entry.reference_number
);

insert into loans (
    tenant_id, organization_id, account_id, loan_code, lender, description,
    original_principal, outstanding_principal, annual_interest_rate, start_date, maturity_date,
    number_of_payments, payment_frequency, status,
    lender_name, loan_name, reference_number, principal_amount, interest_rate,
    monthly_installment, outstanding_balance, starts_on, matures_on, notes
)
select
    context.tenant_id, organization.id, account.id, 'LOAN-ADR-001', 'Razvojna banka demo', 'Financiranje robota i automatizacije.',
    85000.00, 69200.00, 3.25, date '2024-03-01', date '2029-02-28',
    60, 'monthly', 'active',
    'Razvojna banka demo', 'Kredit za automatizaciju', 'KRD-ADR-2024-01', 85000.00, 3.25,
    1650.00, 69200.00, date '2024-03-01', date '2029-02-28', 'Financiranje robota i automatizacije.'
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id and organization.name = 'Adria Dynamics d.o.o. — Osijek Robotika'
join accounts account on account.tenant_id = context.tenant_id and account.organization_id = organization.id and account.code = '2300'
where not exists (select 1 from loans existing where existing.tenant_id = context.tenant_id and existing.reference_number = 'KRD-ADR-2024-01');

insert into loan_installments (tenant_id, loan_id, due_on, principal_amount, interest_amount, status)
select context.tenant_id, loan.id, installment.due_on, installment.principal_amount, installment.interest_amount, installment.status
from nexusops_demo_context context
join loans loan on loan.tenant_id = context.tenant_id and loan.reference_number = 'KRD-ADR-2024-01'
cross join (values
    (current_date - 15,1460.00,190.00,'paid'),
    (current_date + 15,1464.00,186.00,'planned'),
    (current_date + 45,1468.00,182.00,'planned')
) as installment(due_on, principal_amount, interest_amount, status)
on conflict (loan_id, due_on) do update set principal_amount = excluded.principal_amount, interest_amount = excluded.interest_amount, status = excluded.status, updated_at = now();

insert into public_tenders (tenant_id, organization_id, tender_number, title, contracting_authority, source_url, published_on, deadline_at, estimated_value, status, owner_name, notes)
select context.tenant_id, organization.id, tender.number, tender.title, tender.authority, tender.source_url, tender.published_on, tender.deadline_at, tender.estimated_value, tender.status, tender.owner, tender.notes
from nexusops_demo_context context
join organizations organization on organization.tenant_id = context.tenant_id
join (values
    ('Adria Dynamics d.o.o. — Zagreb Operacije','JN-ADR-001','Preventivno održavanje industrijske opreme','Grad Zagreb','https://example.invalid/natjecaj-001',current_date - 10,current_timestamp + interval '18 days',48000.00,'preparing','Ivana Kovačević','Priprema tehničke dokumentacije.'),
    ('Adria Dynamics d.o.o. — Rijeka Logistika','JN-ADR-002','Logistička podrška za javne službe','Primorsko-goranska županija','https://example.invalid/natjecaj-002',current_date - 4,current_timestamp + interval '9 days',72000.00,'submitted','Marko Horvat','Ponuda predana na pregled.'),
    ('Adria Dynamics d.o.o. — Osijek Robotika','JN-ADR-003','Dronovi za inspekciju infrastrukture','Grad Osijek','https://example.invalid/natjecaj-003',current_date - 2,current_timestamp + interval '25 days',110000.00,'monitoring','Tomislav Radić','Praćenje objavljenih pitanja i odgovora.')
) as tender(organization_name, number, title, authority, source_url, published_on, deadline_at, estimated_value, status, owner, notes)
on organization.name = tender.organization_name
on conflict (tenant_id, tender_number) do update set title = excluded.title, deadline_at = excluded.deadline_at, estimated_value = excluded.estimated_value, status = excluded.status, owner_name = excluded.owner_name, notes = excluded.notes, updated_at = now();

insert into public_tender_documents (tenant_id, tender_id, document_name, document_url, document_type)
select context.tenant_id, tender.id, document.name, document.url, document.document_type
from nexusops_demo_context context
join public_tenders tender on tender.tenant_id = context.tenant_id
join (values
    ('JN-ADR-001','Tehnička specifikacija','https://example.invalid/jn-adr-001/specifikacija','specifikacija'),
    ('JN-ADR-002','Predana ponuda','https://example.invalid/jn-adr-002/ponuda','ponuda'),
    ('JN-ADR-003','Objava natječaja','https://example.invalid/jn-adr-003/objava','objava')
) as document(tender_number, name, url, document_type) on tender.tender_number = document.tender_number
where not exists (
    select 1 from public_tender_documents existing
    where existing.tender_id = tender.id and existing.document_name = document.name
);

drop table nexusops_demo_context;
