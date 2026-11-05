// Purpose: The technician's building checklist. Ticking or unticking a building saves it straight away and moves it
// between the "To do" and "Completed" lists (keeping the job's building order), then updates the counts and progress bar.
// If saving fails the tick is undone and an error is shown. Without scripts, each building has its own Save button.
(() => {
    const checklist = document.querySelector('[data-checklist]');
    if (!checklist) return;

    const lists = {
        todo: checklist.querySelector('[data-list="todo"]'),
        done: checklist.querySelector('[data-list="done"]')
    };
    const error = checklist.querySelector('[data-checklist-error]');
    if (!lists.todo || !lists.done) return;

    const refresh = () => {
        const counts = { todo: lists.todo.children.length, done: lists.done.children.length };
        const total = counts.todo + counts.done;
        const percent = total === 0 ? 0 : Math.floor(counts.done * 100 / total);
        for (const key of ['todo', 'done']) {
            checklist.querySelector(`[data-count="${key}"]`).textContent = counts[key];
            checklist.querySelector(`[data-empty="${key}"]`).hidden = counts[key] > 0;
        }
        checklist.querySelector('[data-progress-text]').textContent =
            `${counts.done} of ${total} building${total === 1 ? '' : 's'} complete`;
        checklist.querySelector('[data-progress-percent]').textContent = `${percent}%`;
        checklist.querySelector('[data-progress-bar]').style.width = `${percent}%`;
        checklist.querySelector('[role="progressbar"]').setAttribute('aria-valuenow', percent);
    };

    // Puts the item before the first item that comes after it in the job's building order.
    const place = (item, list) => {
        const position = Number(item.dataset.position);
        const next = [...list.children].find(other => Number(other.dataset.position) > position);
        list.insertBefore(item, next ?? null);
    };

    checklist.addEventListener('change', async event => {
        const box = event.target.closest('.checklist-check');
        if (!box) return;
        const item = box.closest('[data-position]');
        // Read the form before disabling the box: disabled inputs are left out of the posted data.
        const body = new FormData(box.form);
        box.disabled = true;
        error.hidden = true;
        try {
            const response = await fetch(box.form.action, { method: 'POST', body, headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const result = await response.json();
            box.checked = result.isComplete;
            item.querySelector('[data-completed-text]').textContent = result.completedText;
            place(item, result.isComplete ? lists.done : lists.todo);
            refresh();
        } catch {
            box.checked = !box.checked;
            error.textContent = "Couldn't save that change. Check your connection and try again.";
            error.hidden = false;
        } finally {
            box.disabled = false;
            box.focus();
        }
    });
})();
