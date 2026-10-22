// Purpose: Stores technicians in technicians.json via JsonFileStore.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public class JsonTechnicianRepository : ITechnicianRepository
{
    private readonly JsonFileStore<Technician> _store;

    public JsonTechnicianRepository(JsonFileStore<Technician> store)
    {
        _store = store;
    }

    public Task<List<Technician>> GetAllAsync() => _store.ReadAllAsync();

    public async Task<Technician?> GetByIdAsync(Guid id) =>
        (await _store.ReadAllAsync()).FirstOrDefault(t => t.Id == id);

    public Task AddAsync(Technician technician) =>
        _store.UpdateAsync(items =>
        {
            items.Add(technician);
            return true;
        });

    public Task<bool> UpdateAsync(Technician technician) =>
        _store.UpdateAsync(items =>
        {
            var index = items.FindIndex(t => t.Id == technician.Id);
            if (index < 0)
            {
                return false;
            }

            items[index] = technician;
            return true;
        });

    public Task<bool> DeleteAsync(Guid id) =>
        _store.UpdateAsync(items => items.RemoveAll(t => t.Id == id) > 0);
}
