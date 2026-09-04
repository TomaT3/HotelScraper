import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  createTenant,
  createUser,
  getCities,
  getTenants,
  getUsers,
  patchTenant,
  patchUser,
  resetUserPassword,
} from "../api/client";
import type { City, Tenant, UserAdmin } from "../api/types";

/**
 * Extracts the backend `detail` field from an API error message
 * ("API error 400: {\"detail\":\"...\"}") for inline display.
 */
function errorDetail(err: unknown): string {
  if (err instanceof Error) {
    const m = err.message.match(/API error \d+: (.+)/);
    if (m) {
      try {
        const parsed = JSON.parse(m[1]);
        if (parsed && typeof parsed.detail === "string") return parsed.detail;
      } catch {
        // fall through to raw message
      }
      return m[1];
    }
    return err.message;
  }
  return String(err);
}

interface TenantForm {
  name: string;
  cities: string[];
  is_active: boolean;
}

const emptyTenantForm: TenantForm = { name: "", cities: [], is_active: true };

interface UserForm {
  email: string;
  password: string;
  role: "admin" | "user";
  tenant_id: number | null;
  is_active: boolean;
}

const emptyUserForm: UserForm = {
  email: "",
  password: "",
  role: "user",
  tenant_id: null,
  is_active: true,
};

interface Props {
  currentUserId: number;
}

export default function AdminView({ currentUserId }: Props) {
  const [tab, setTab] = useState<"tenants" | "users">("tenants");

  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [users, setUsers] = useState<UserAdmin[]>([]);
  const [cities, setCities] = useState<City[]>([]);

  const [loadingTenants, setLoadingTenants] = useState(true);
  const [loadingUsers, setLoadingUsers] = useState(true);

  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Tenant form (create + edit)
  const [tenantForm, setTenantForm] = useState<TenantForm>(emptyTenantForm);
  const [editingTenantId, setEditingTenantId] = useState<number | null>(null);

  // User form (create)
  const [userForm, setUserForm] = useState<UserForm>(emptyUserForm);

  const reloadTenants = useCallback(async () => {
    setLoadingTenants(true);
    try {
      setTenants(await getTenants());
    } catch (err) {
      setError(`Tenants laden fehlgeschlagen: ${errorDetail(err)}`);
    } finally {
      setLoadingTenants(false);
    }
  }, []);

  const reloadUsers = useCallback(async () => {
    setLoadingUsers(true);
    try {
      setUsers(await getUsers());
    } catch (err) {
      setError(`Benutzer laden fehlgeschlagen: ${errorDetail(err)}`);
    } finally {
      setLoadingUsers(false);
    }
  }, []);

  useEffect(() => {
    // Cities are the same for both tabs — load once (admin sees all cities)
    getCities()
      .then(setCities)
      .catch((err) => setError(`Städte laden fehlgeschlagen: ${errorDetail(err)}`));
    reloadTenants();
    reloadUsers();
  }, [reloadTenants, reloadUsers]);

  const activeTenants = tenants.filter((t) => t.is_active);

  // ── Tenants ──────────────────────────────────────────────────────────

  const startEditTenant = (t: Tenant) => {
    setEditingTenantId(t.id);
    setTenantForm({ name: t.name, cities: [...t.cities], is_active: t.is_active });
    setError(null);
    setSuccess(null);
  };

  const cancelEditTenant = () => {
    setEditingTenantId(null);
    setTenantForm(emptyTenantForm);
  };

  const toggleCity = (city: string, checked: boolean) => {
    setTenantForm((f) => ({
      ...f,
      cities: checked
        ? [...f.cities, city]
        : f.cities.filter((c) => c !== city),
    }));
  };

  const handleTenantSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (!tenantForm.name.trim()) {
      setError("Bitte einen Namen angeben.");
      return;
    }
    if (tenantForm.cities.length === 0) {
      setError("Bitte mindestens eine Stadt auswählen.");
      return;
    }

    setSubmitting(true);
    try {
      if (editingTenantId !== null) {
        await patchTenant(editingTenantId, tenantForm);
        setSuccess("Tenant aktualisiert.");
      } else {
        await createTenant(tenantForm);
        setSuccess("Tenant angelegt.");
      }
      setTenantForm(emptyTenantForm);
      setEditingTenantId(null);
      await reloadTenants();
    } catch (err) {
      setError(`Speichern fehlgeschlagen: ${errorDetail(err)}`);
    } finally {
      setSubmitting(false);
    }
  };

  // ── Users ────────────────────────────────────────────────────────────

  const handleUserSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (!userForm.email.trim()) {
      setError("Bitte eine E-Mail-Adresse angeben.");
      return;
    }
    if (userForm.password.length < 8) {
      setError("Das Passwort muss mindestens 8 Zeichen lang sein.");
      return;
    }
    if (userForm.role === "user" && userForm.tenant_id === null) {
      setError("Für die Rolle 'Benutzer' muss ein Tenant gewählt werden.");
      return;
    }

    setSubmitting(true);
    try {
      await createUser({
        email: userForm.email,
        password: userForm.password,
        role: userForm.role,
        tenant_id: userForm.role === "user" ? userForm.tenant_id : null,
        is_active: userForm.is_active,
      });
      setSuccess("Benutzer angelegt.");
      setUserForm(emptyUserForm);
      await reloadUsers();
    } catch (err) {
      setError(`Anlegen fehlgeschlagen: ${errorDetail(err)}`);
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleUser = async (u: UserAdmin) => {
    setError(null);
    setSuccess(null);
    try {
      const updated = await patchUser(u.id, { is_active: !u.is_active });
      setSuccess(
        `Benutzer ${updated.email} ${updated.is_active ? "aktiviert" : "deaktiviert"}.`
      );
      await reloadUsers();
    } catch (err) {
      setError(`Aktion fehlgeschlagen: ${errorDetail(err)}`);
    }
  };

  const handleResetPassword = async (u: UserAdmin) => {
    const password = window.prompt(`Neues Passwort für ${u.email}:`);
    if (!password) return;
    if (password.length < 8) {
      setError("Das Passwort muss mindestens 8 Zeichen lang sein.");
      return;
    }
    setError(null);
    setSuccess(null);
    try {
      await resetUserPassword(u.id, password);
      setSuccess(`Passwort für ${u.email} zurückgesetzt.`);
    } catch (err) {
      setError(`Passwort zurücksetzen fehlgeschlagen: ${errorDetail(err)}`);
    }
  };

  // ── Render helpers ───────────────────────────────────────────────────

  const badge = (active: boolean, activeLabel: string, inactiveLabel: string) => (
    <span
      className={`px-2 py-0.5 rounded-none text-xs font-mono uppercase tracking-label-sm ${
        active ? "text-success" : "text-muted"
      }`}
    >
      {active ? activeLabel : inactiveLabel}
    </span>
  );

  const tabButton = (id: "tenants" | "users", label: string) => (
    <button
      onClick={() => {
        setTab(id);
        setError(null);
        setSuccess(null);
      }}
      aria-pressed={tab === id}
      className={`px-4 py-2 rounded-pill text-xs font-mono uppercase tracking-label-sm transition-colors ${
        tab === id
          ? "border border-ink text-ink bg-surface-elevated"
          : "border border-hairline-strong text-muted hover:text-body"
      }`}
    >
      {label}
    </button>
  );

  const inputClass =
    "mt-1 w-full py-2 bg-canvas border-0 border-b border-hairline-strong rounded-none text-sm text-ink placeholder:text-muted-soft focus:border-ink";
  const labelClass = "block font-mono text-xs uppercase tracking-label-sm text-muted";

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3 flex-wrap">
        <h2 className="font-display uppercase tracking-display-md text-ink">Verwaltung</h2>
        <div className="flex gap-2">
          {tabButton("tenants", "Tenants")}
          {tabButton("users", "Benutzer")}
        </div>
      </div>

      {error && (
        <div className="text-sm text-danger border border-hairline-strong rounded-none px-3 py-2">
          {error}
        </div>
      )}
      {success && (
        <div className="text-sm text-success border border-hairline-strong rounded-none px-3 py-2">
          {success}
        </div>
      )}

      {tab === "tenants" ? (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
          {/* Tenant list */}
          <div className="lg:col-span-2">
            <div className="bg-surface-card border border-hairline rounded-none overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-surface-soft text-left text-xs uppercase tracking-wide text-muted">
                  <tr>
                    <th className="px-4 py-3">Name</th>
                    <th className="px-4 py-3">Städte</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3 text-right">Aktionen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-hairline">
                  {loadingTenants ? (
                    <tr>
                      <td colSpan={4} className="px-4 py-6 text-center text-muted">
                        Wird geladen…
                      </td>
                    </tr>
                  ) : tenants.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="px-4 py-6 text-center text-muted">
                        Keine Tenants vorhanden.
                      </td>
                    </tr>
                  ) : (
                    tenants.map((t) => (
                      <tr key={t.id} className="hover:bg-surface-elevated">
                        <td className="px-4 py-3 text-body-strong">{t.name}</td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap gap-1">
                            {t.cities.map((c) => (
                              <span
                                key={c}
                                className="px-2 py-0.5 rounded-none font-mono text-xs text-link"
                              >
                                {c}
                              </span>
                            ))}
                          </div>
                        </td>
                        <td className="px-4 py-3">{badge(t.is_active, "aktiv", "inaktiv")}</td>
                        <td className="px-4 py-3 text-right">
                          <button
                            onClick={() => startEditTenant(t)}
                            className="px-2 py-1 text-xs border border-hairline-strong rounded-pill font-mono uppercase tracking-label-sm text-muted hover:text-body transition-colors"
                          >
                            Bearbeiten
                          </button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Tenant form */}
          <div>
            <form onSubmit={handleTenantSubmit} className="bg-surface-card border border-hairline rounded-none p-5 space-y-4">
              <h3 className="font-display uppercase tracking-display-md text-ink">
                {editingTenantId !== null ? "Tenant bearbeiten" : "Neuer Tenant"}
              </h3>
              <div>
                <label htmlFor="tenant-name" className={labelClass}>
                  Name
                </label>
                <input
                  id="tenant-name"
                  type="text"
                  required
                  value={tenantForm.name}
                  onChange={(e) => setTenantForm((f) => ({ ...f, name: e.target.value }))}
                  className={inputClass}
                  placeholder="z.B. Stuttgart Hotels GmbH"
                />
              </div>
              <div>
                <span className={labelClass}>Städte</span>
                <div className="mt-2 space-y-1.5 max-h-48 overflow-y-auto">
                  {cities.length === 0 && (
                    <div className="text-xs text-muted">Keine Städte konfiguriert.</div>
                  )}
                  {cities.map((c) => (
                    <label key={c.name} className="flex items-center gap-2 text-sm text-body">
                      <input
                        type="checkbox"
                        checked={tenantForm.cities.includes(c.name)}
                        onChange={(e) => toggleCity(c.name, e.target.checked)}
                        className="rounded border-hairline text-link"
                      />
                      {c.name}
                    </label>
                  ))}
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm text-body">
                <input
                  type="checkbox"
                  checked={tenantForm.is_active}
                  onChange={(e) => setTenantForm((f) => ({ ...f, is_active: e.target.checked }))}
                  className="rounded border-hairline text-link"
                />
                Aktiv
              </label>
              <div className="flex gap-2">
                <button
                  type="submit"
                  disabled={submitting}
                  className="flex-1 px-4 py-2 border border-ink text-ink rounded-pill font-mono uppercase tracking-label text-sm hover:bg-ink hover:text-canvas transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {submitting
                    ? "Speichern…"
                    : editingTenantId !== null
                      ? "Speichern"
                      : "Anlegen"}
                </button>
                {editingTenantId !== null && (
                  <button
                    type="button"
                    onClick={cancelEditTenant}
                    className="px-4 py-2 text-sm border border-hairline-strong rounded-pill font-mono uppercase tracking-label text-muted hover:text-body transition-colors"
                  >
                    Abbrechen
                  </button>
                )}
              </div>
            </form>
          </div>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
          {/* User list */}
          <div className="lg:col-span-2">
            <div className="bg-surface-card border border-hairline rounded-none overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-surface-soft text-left text-xs uppercase tracking-wide text-muted">
                  <tr>
                    <th className="px-4 py-3">E-Mail</th>
                    <th className="px-4 py-3">Rolle</th>
                    <th className="px-4 py-3">Tenant</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3 text-right">Aktionen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-hairline">
                  {loadingUsers ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-6 text-center text-muted">
                        Wird geladen…
                      </td>
                    </tr>
                  ) : users.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-6 text-center text-muted">
                        Keine Benutzer vorhanden.
                      </td>
                    </tr>
                  ) : (
                    users.map((u) => {
                      const isSelf = u.id === currentUserId;
                      return (
                        <tr key={u.id} className="hover:bg-surface-elevated">
                          <td className="px-4 py-3 text-body-strong">
                            {u.email}
                            {isSelf && (
                              <span className="ml-2 px-2 py-0.5 rounded-none font-mono uppercase tracking-label-sm text-xs text-warning">
                                eigenes Konto
                              </span>
                            )}
                          </td>
                          <td className="px-4 py-3">
                            <span
                              className={`px-2 py-0.5 rounded-none font-mono uppercase tracking-label-sm text-xs ${
                                u.role === "admin"
                                  ? "text-link"
                                  : "text-muted"
                              }`}
                            >
                              {u.role === "admin" ? "Admin" : "Benutzer"}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-body">{u.tenant_name ?? "—"}</td>
                          <td className="px-4 py-3">{badge(u.is_active, "aktiv", "inaktiv")}</td>
                          <td className="px-4 py-3">
                            <div className="flex items-center justify-end gap-2">
                              <button
                                onClick={() => handleToggleUser(u)}
                                disabled={isSelf && u.is_active}
                                title={isSelf ? "Das eigene Konto kann nicht deaktiviert werden" : undefined}
                                className={`px-2 py-1 text-xs border rounded-pill font-mono uppercase tracking-label-sm transition-colors ${
                                  isSelf && u.is_active
                                    ? "border-hairline text-muted-soft cursor-not-allowed"
                                    : "border-hairline-strong text-muted hover:text-body"
                                }`}
                              >
                                {u.is_active ? "Deaktivieren" : "Aktivieren"}
                              </button>
                              <button
                                onClick={() => handleResetPassword(u)}
                                className="px-2 py-1 text-xs border border-hairline-strong rounded-pill font-mono uppercase tracking-label-sm text-muted hover:text-body transition-colors"
                              >
                                Passwort zurücksetzen
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* User create form */}
          <div>
            <form onSubmit={handleUserSubmit} className="bg-surface-card border border-hairline rounded-none p-5 space-y-4">
              <h3 className="font-display uppercase tracking-display-md text-ink">Neuer Benutzer</h3>
              <div>
                <label htmlFor="user-email" className={labelClass}>
                  E-Mail
                </label>
                <input
                  id="user-email"
                  type="email"
                  required
                  autoComplete="off"
                  value={userForm.email}
                  onChange={(e) => setUserForm((f) => ({ ...f, email: e.target.value }))}
                  className={inputClass}
                  placeholder="name@hotel.de"
                />
              </div>
              <div>
                <label htmlFor="user-password" className={labelClass}>
                  Passwort
                </label>
                <input
                  id="user-password"
                  type="password"
                  required
                  autoComplete="new-password"
                  value={userForm.password}
                  onChange={(e) => setUserForm((f) => ({ ...f, password: e.target.value }))}
                  className={inputClass}
                  placeholder="mindestens 8 Zeichen"
                />
              </div>
              <div>
                <label htmlFor="user-role" className={labelClass}>
                  Rolle
                </label>
                <select
                  id="user-role"
                  value={userForm.role}
                  onChange={(e) =>
                    setUserForm((f) => ({
                      ...f,
                      role: e.target.value as "admin" | "user",
                      tenant_id: e.target.value === "admin" ? null : f.tenant_id,
                    }))
                  }
                  className={inputClass}
                >
                  <option value="user">Benutzer</option>
                  <option value="admin">Admin</option>
                </select>
              </div>
              {userForm.role === "user" && (
                <div>
                  <label htmlFor="user-tenant" className={labelClass}>
                    Tenant
                  </label>
                  <select
                    id="user-tenant"
                    required
                    value={userForm.tenant_id ?? ""}
                    onChange={(e) =>
                      setUserForm((f) => ({
                        ...f,
                        tenant_id: e.target.value ? Number(e.target.value) : null,
                      }))
                    }
                    className={inputClass}
                  >
                    <option value="" disabled>
                      Tenant wählen…
                    </option>
                    {activeTenants.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <label className="flex items-center gap-2 text-sm text-body">
                <input
                  type="checkbox"
                  checked={userForm.is_active}
                  onChange={(e) => setUserForm((f) => ({ ...f, is_active: e.target.checked }))}
                  className="rounded border-hairline text-link"
                />
                Aktiv
              </label>
              <button
                type="submit"
                disabled={submitting}
                className="w-full px-4 py-2 border border-ink text-ink rounded-pill font-mono uppercase tracking-label text-sm hover:bg-ink hover:text-canvas transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {submitting ? "Anlegen…" : "Benutzer anlegen"}
              </button>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
