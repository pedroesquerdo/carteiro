const form = document.querySelector("#email-form");
const submitButton = document.querySelector("#submit-button");
const refreshButton = document.querySelector("#refresh-button");
const formMessage = document.querySelector("#form-message");
const emailList = document.querySelector("#email-list");

const statusLabels = {
    sent: "Enviado",
    failed: "Falhou",
    pending: "Pendente",
    queued: "Na fila",
    processing: "Em trânsito"
};

function escapeHtml(value) {
    const element = document.createElement("div");
    element.textContent = value ?? "";
    return element.innerHTML;
}

function formatDate(value) {
    return new Intl.DateTimeFormat("pt-BR", {
        dateStyle: "short",
        timeStyle: "short"
    }).format(new Date(value));
}

function showMessage(text, type) {
    formMessage.textContent = text;
    formMessage.className = `form-message ${type}`;
}

async function loadEmails() {
    refreshButton.disabled = true;

    try {
        const response = await fetch("/emails");
        if (!response.ok) throw new Error("Não foi possível carregar o histórico.");

        const emails = await response.json();
        if (emails.length === 0) {
            emailList.innerHTML = '<p class="empty-state">Nenhum envio registrado ainda.</p>';
            return;
        }

        emailList.innerHTML = emails.map(email => `
            <article class="email-item">
                <div class="email-topline">
                    <span class="email-to">${escapeHtml(email.to)}</span>
                    <span class="badge ${escapeHtml(email.status)}">${statusLabels[email.status] ?? email.status}</span>
                </div>
                <p class="email-subject">${escapeHtml(email.subject)}</p>
                <p class="email-meta">#${email.id} · ${formatDate(email.createdAt)}</p>
                ${email.errorMessage ? `<p class="email-error">${escapeHtml(email.errorMessage)}</p>` : ""}
            </article>
        `).join("");
    } catch (error) {
        emailList.innerHTML = `<p class="empty-state">${escapeHtml(error.message)}</p>`;
    } finally {
        refreshButton.disabled = false;
    }
}

form.addEventListener("submit", async event => {
    event.preventDefault();
    submitButton.disabled = true;
    submitButton.firstChild.textContent = "Enviando... ";
    formMessage.className = "form-message";

    const data = new FormData(form);
    const payload = Object.fromEntries(data.entries());

    try {
        const response = await fetch("/emails", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });
        const result = await response.json();

        if (!response.ok) {
            throw new Error(result.detail ?? result.error ?? "Falha ao enviar o e-mail.");
        }

        showMessage(`Remessa #${result.id} recebida e colocada na fila.`, "success");
        form.reset();
        await loadEmails();
    } catch (error) {
        showMessage(error.message, "error");
        await loadEmails();
    } finally {
        submitButton.disabled = false;
        submitButton.firstChild.textContent = "Despachar mensagem ";
    }
});

refreshButton.addEventListener("click", loadEmails);
loadEmails();
setInterval(loadEmails, 5000);
