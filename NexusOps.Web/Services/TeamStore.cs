using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public interface ITeamStore
{
    IReadOnlyList<TeamMember> ListToday();
    TeamMember? GetForAuthUser(Guid authUserId);
    void UpdateStatus(Guid employeeId, string status, string? location, string? task, bool doNotDisturb, string? actor);
    void StartWork(Guid employeeId, string? actor);
    void EndWork(Guid employeeId, string? actor);
}

public sealed class PostgresTeamStore(NpgsqlDataSource dataSource, Guid tenantId) : ITeamStore
{
    private const string MemberColumns = "e.id,coalesce(e.full_name,concat(e.first_name,' ',e.last_name)),coalesce(e.job_title,''),coalesce(p.presence_status,'off_duty'),p.work_location,p.current_task,coalesce(p.do_not_disturb,false),p.status_started_at,coalesce(w.work_mode,'clocked'),(select t.started_at from employee_time_entries t where t.employee_id=e.id and t.ended_at is null order by t.started_at desc limit 1)";
    private const string MemberJoins = " from employees e left join employee_presence p on p.employee_id=e.id left join employee_work_policies w on w.employee_id=e.id ";

    public IReadOnlyList<TeamMember> ListToday()
    {
        using var command = dataSource.CreateCommand($"select {MemberColumns}{MemberJoins}where e.tenant_id=$1 order by 2");
        command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader(); var members = new List<TeamMember>();
        while (reader.Read()) members.Add(ReadMember(reader));
        return members;
    }

    public TeamMember? GetForAuthUser(Guid authUserId)
    {
        using var command = dataSource.CreateCommand($"select {MemberColumns}{MemberJoins}where e.tenant_id=$1 and e.auth_user_id=$2 limit 1");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(authUserId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMember(reader) : null;
    }

    public void UpdateStatus(Guid id, string status, string? location, string? task, bool dnd, string? actor)
    {
        using var command = dataSource.CreateCommand("insert into employee_presence (tenant_id,employee_id,presence_status,work_location,current_task,do_not_disturb,status_started_at,updated_by_name) values ($1,$2,$3,$4,$5,$6,now(),$7) on conflict (employee_id) do update set presence_status=excluded.presence_status,work_location=excluded.work_location,current_task=excluded.current_task,do_not_disturb=excluded.do_not_disturb,status_started_at=now(),updated_by_name=excluded.updated_by_name,updated_at=now()");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(status); command.Parameters.AddWithValue((object?)location ?? DBNull.Value); command.Parameters.AddWithValue((object?)task ?? DBNull.Value); command.Parameters.AddWithValue(dnd); command.Parameters.AddWithValue((object?)actor ?? DBNull.Value); command.ExecuteNonQuery();
    }

    public void StartWork(Guid id, string? actor)
    {
        using var command = dataSource.CreateCommand("insert into employee_time_entries (tenant_id,employee_id,started_at,entry_source,note) select $1,$2,now(),'self_service',$3 where not exists (select 1 from employee_time_entries where employee_id=$2 and ended_at is null)");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue((object?)actor ?? DBNull.Value); command.ExecuteNonQuery(); UpdateStatus(id, "working", null, null, false, actor);
    }

    public void EndWork(Guid id, string? actor)
    {
        using var command = dataSource.CreateCommand("update employee_time_entries set ended_at=now(),updated_at=now(),approved_by_name=$2 where employee_id=$1 and ended_at is null");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue((object?)actor ?? DBNull.Value); command.ExecuteNonQuery(); UpdateStatus(id, "off_duty", null, null, false, actor);
    }

    private static TeamMember ReadMember(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
}

public sealed class InMemoryTeamStore : ITeamStore
{
    private readonly List<TeamMember> members = [new(Guid.NewGuid(), "Ivana Kovačević", "Direktorica", "focused", "Zagreb", "Pregled ponude", true, DateTimeOffset.UtcNow, "trust", null), new(Guid.NewGuid(), "Luka Marić", "Voditelj održavanja", "on_site", "Pogon A", "Dijagnostika CNC-a", false, DateTimeOffset.UtcNow, "clocked", DateTimeOffset.UtcNow.AddHours(-2))];
    public IReadOnlyList<TeamMember> ListToday() => members;
    public TeamMember? GetForAuthUser(Guid authUserId) => members.FirstOrDefault();
    public void UpdateStatus(Guid id, string status, string? location, string? task, bool dnd, string? actor) { var member = members.First(item => item.EmployeeId == id); members[members.IndexOf(member)] = member with { Status = status, Location = location ?? member.Location, Task = task ?? member.Task, DoNotDisturb = dnd, StartedAt = DateTimeOffset.UtcNow }; }
    public void StartWork(Guid id, string? actor) => UpdateStatus(id, "working", null, null, false, actor);
    public void EndWork(Guid id, string? actor) => UpdateStatus(id, "off_duty", null, null, false, actor);
}
