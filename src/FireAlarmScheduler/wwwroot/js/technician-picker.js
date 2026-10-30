// Job form technician picker: keeps the dropdown's summary and the conflict warning in step with the ticked
// technicians, and re-checks everyone's availability with the server when the job's dates or daily hours change.
(() => {
    const picker = document.querySelector('.technician-picker[data-availability-url]');
    const choices = picker ? [...picker.querySelectorAll('.technician-choice')] : [];
    if (choices.length === 0) return;

    const form = picker.closest('form');
    const summary = picker.querySelector('[data-picker-summary]');
    const note = picker.querySelector('[data-picker-note]');
    const warning = picker.querySelector('[data-picker-warning]');
    const warningList = picker.querySelector('[data-picker-warning-list]');
    const badgeText = { free: 'Free', conflict: 'Conflict', unknown: 'Not checked' };
    const conflictsOf = choice => [...choice.querySelectorAll('[data-conflicts] > span')].map(line => line.textContent);

    const refreshSelection = () => {
        const chosen = choices.filter(choice => choice.querySelector('input').checked);
        summary.textContent = chosen.length ? chosen.map(choice => choice.dataset.name).join(', ') : 'Choose technicians';
        const clashing = chosen.filter(choice => choice.dataset.status === 'conflict');
        warningList.replaceChildren(...clashing.map(choice => {
            const item = document.createElement('li');
            const name = document.createElement('strong');
            name.textContent = choice.dataset.name;
            item.append(name, `: ${conflictsOf(choice).join('; ')}`);
            return item;
        }));
        warning.hidden = clashing.length === 0;
    };

    const setStatus = (choice, status, conflicts) => {
        choice.dataset.status = status;
        choice.querySelector('[data-status-badge]').textContent = badgeText[status];
        choice.querySelector('[data-conflicts]').replaceChildren(...conflicts.map(text => {
            const line = document.createElement('span');
            line.className = 'd-block';
            line.textContent = text;
            return line;
        }));
    };

    // Only the newest check is applied, so quick successive edits can't leave stale marks behind.
    let latestCheck = 0;
    const refreshAvailability = async () => {
        const value = name => form.elements[name].value;
        const query = new URLSearchParams({
            startDate: value('StartDate'), endDate: value('EndDate'), startTime: value('StartTime'), endTime: value('EndTime')
        });
        if (picker.dataset.jobId) query.set('jobId', picker.dataset.jobId);

        const check = ++latestCheck;
        let results = null;
        try {
            const response = await fetch(`${picker.dataset.availabilityUrl}?${query}`, { headers: { Accept: 'application/json' } });
            if (response.ok) results = await response.json();
        } catch {
            // Offline or server error: the technicians are shown as "Not checked" below.
        }
        if (check !== latestCheck) return;

        const conflictsById = new Map((results ?? []).map(result => [result.id, result.conflicts]));
        choices.forEach(choice => {
            const conflicts = conflictsById.get(choice.querySelector('input').value);
            setStatus(choice, !conflicts ? 'unknown' : conflicts.length ? 'conflict' : 'free', conflicts ?? []);
        });
        note.textContent = results ? 'Availability for the dates and hours above.' : 'Enter valid dates and hours to check availability.';
        refreshSelection();
    };

    picker.addEventListener('change', event => {
        if (event.target.name === 'AssignedTechnicianIds') refreshSelection();
    });
    ['StartDate', 'EndDate', 'StartTime', 'EndTime'].forEach(name => form.elements[name].addEventListener('change', refreshAvailability));
})();
