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
      className={`px-2 py-0.5 rounded-full text-xs font-medium ${
        active ? "bg-green-100 text-green-700" : "bg-gray-100 text-gray-500"
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
      className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
        tab === id
          ? "bg-blue-600 text-white shadow-sm"
          : "bg-white text-gray-600 hover:bg-gray-100 border border-gray-200"
      }`}
    >
      {label}
    </button>
  );

  const inputClass =
    "mt-1 w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500";
  const labelClass = "block text-sm font-medium text-gray-700";

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3 flex-wrap">
        <h2 className="text-lg font-bold text-gray-800">Verwaltung</h2>
        <div className="flex gap-2">
          {tabButton("tenants", "Tenants")}
          {tabButton("users", "Benutzer")}
        </div>
      </div>

      {error && (
        <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {error}
        </div>
      )}
      {success && (
        <div className="text-sm text-green-700 bg-green-50 border border-green-200 rounded-lg px-3 py-2">
          {success}
        </div>
      )}

      {tab === "tenants" ? (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
          {/* Tenant list */}
          <div className="lg:col-span-2">
            <div className="bg-white rounded-xl shadow overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-500">
                  <tr>
                    <th className="px-4 py-3">Name</th>
                    <th className="px-4 py-3">Städte</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3 text-right">Aktionen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {loadingTenants ? (
                    <tr>
                      <td colSpan={4} className="px-4 py-6 text-center text-gray-400">
                        Wird geladen…
                      </td>
                    </tr>
                  ) : tenants.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="px-4 py-6 text-center text-gray-400">
                        Keine Tenants vorhanden.
                      </td>
                    </tr>
                  ) : (
                    tenants.map((t) => (
                      <tr key={t.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3 font-medium text-gray-800">{t.name}</td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap gap-1">
                            {t.cities.map((c) => (
                              <span
                                key={c}
                                className="px-2 py-0.5 rounded-full bg-blue-50 text-blue-700 text-xs"
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
                            className="px-2 py-1 text-xs border border-gray-300 rounded-lg hover:bg-gray-100 text-gray-600 transition-colors"
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
            <form onSubmit={handleTenantSubmit} className="bg-white rounded-xl shadow p-5 space-y-4">
              <h3 className="font-semibold text-gray-800">
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
                    <div className="text-xs text-gray-400">Keine Städte konfiguriert.</div>
                  )}
                  {cities.map((c) => (
                    <label key={c.name} className="flex items-center gap-2 text-sm text-gray-700">
                      <input
                        type="checkbox"
                        checked={tenantForm.cities.includes(c.name)}
                        onChange={(e) => toggleCity(c.name, e.target.checked)}
                        className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                      />
                      {c.name}
                    </label>
                  ))}
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  checked={tenantForm.is_active}
                  onChange={(e) => setTenantForm((f) => ({ ...f, is_active: e.target.checked }))}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                Aktiv
              </label>
              <div className="flex gap-2">
                <button
                  type="submit"
                  disabled={submitting}
                  className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-medium transition-colors"
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
                    className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-100 text-gray-600 transition-colors"
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
            <div className="bg-white rounded-xl shadow overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-500">
                  <tr>
                    <th className="px-4 py-3">E-Mail</th>
                    <th className="px-4 py-3">Rolle</th>
                    <th className="px-4 py-3">Tenant</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3 text-right">Aktionen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {loadingUsers ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-6 text-center text-gray-400">
                        Wird geladen…
                      </td>
                    </tr>
                  ) : users.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-6 text-center text-gray-400">
                        Keine Benutzer vorhanden.
                      </td>
                    </tr>
                  ) : (
                    users.map((u) => {
                      const isSelf = u.id === currentUserId;
                      return (
                        <tr key={u.id} className="hover:bg-gray-50">
                          <td className="px-4 py-3 font-medium text-gray-800">
                            {u.email}
                            {isSelf && (
                              <span className="ml-2 px-2 py-0.5 rounded-full bg-purple-100 text-purple-700 text-xs">
                                eigenes Konto
                              </span>
                            )}
                          </td>
                          <td className="px-4 py-3">
                            <span
                              className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                                u.role === "admin"
                                  ? "bg-indigo-100 text-indigo-700"
                                  : "bg-gray-100 text-gray-600"
                              }`}
                            >
                              {u.role === "admin" ? "Admin" : "Benutzer"}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-gray-600">{u.tenant_name ?? "—"}</td>
                          <td className="px-4 py-3">{badge(u.is_active, "aktiv", "inaktiv")}</td>
                          <td className="px-4 py-3">
                            <div className="flex items-center justify-end gap-2">
                              <button
                                onClick={() => handleToggleUser(u)}
                                disabled={isSelf && u.is_active}
                                title={isSelf ? "Das eigene Konto kann nicht deaktiviert werden" : undefined}
                                className={`px-2 py-1 text-xs border rounded-lg transition-colors ${
                                  isSelf && u.is_active
                                    ? "border-gray-200 text-gray-300 cursor-not-allowed"
                                    : "border-gray-300 hover:bg-gray-100 text-gray-600"
                                }`}
                              >
                                {u.is_active ? "Deaktivieren" : "Aktivieren"}
                              </button>
                              <button
                                onClick={() => handleResetPassword(u)}
                                className="px-2 py-1 text-xs border border-gray-300 rounded-lg hover:bg-gray-100 text-gray-600 transition-colors"
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
            <form onSubmit={handleUserSubmit} className="bg-white rounded-xl shadow p-5 space-y-4">
              <h3 className="font-semibold text-gray-800">Neuer Benutzer</h3>
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
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  checked={userForm.is_active}
                  onChange={(e) => setUserForm((f) => ({ ...f, is_active: e.target.checked }))}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                Aktiv
              </label>
              <button
                type="submit"
                disabled={submitting}
                className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed text-sm font-medium transition-colors"
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
