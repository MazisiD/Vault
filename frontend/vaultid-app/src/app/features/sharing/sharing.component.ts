import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of } from 'rxjs';
import { VaultApiService } from '../../services/vault-api.service';
import { SessionService } from '../../services/session.service';
import {
  Category, FieldDefinition, GeneratedShareCode, Grant, Organisation, PendingShareRequest,
  ShareCode, ShareCodeStatus,
} from '../../models';
import { categoryMeta, fieldMeta, grantStatusMeta } from '../../ui-meta';

/** A category with its leaf fields flattened, as rendered in the share picker. */
interface PickerCategory {
  id: string;
  name: string;
  fields: { id: string; label: string }[];
}

/**
 * Sharing view. The user picks exactly which fields an organisation may read
 * and generates a share code; the organisation redeems that code out-of-band,
 * which raises a request the user approves (after reading the organisation's
 * data-processing agreement) or rejects. Nothing is released until approval.
 * Pure UI over the API - all rules live in the backend.
 */
@Component({
  selector: 'app-sharing',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './sharing.component.html',
  styleUrl: './sharing.component.css',
})
export class SharingComponent {
  private readonly api = inject(VaultApiService);
  private readonly session = inject(SessionService);

  readonly categories = signal<Category[]>([]);
  readonly organisations = signal<Organisation[]>([]);
  /** Status by organisation id, from the unfiltered directory, for the shares table. */
  readonly orgStatuses = signal<ReadonlyMap<string, string>>(new Map());
  readonly grants = signal<Grant[]>([]);
  readonly codes = signal<ShareCode[]>([]);
  readonly pending = signal<PendingShareRequest[]>([]);

  readonly shareOpen = signal(false);
  readonly orgOpen = signal(false);
  readonly generated = signal<GeneratedShareCode | null>(null);
  readonly editingGrant = signal<Grant | null>(null);
  readonly selected = signal<ReadonlySet<string>>(new Set<string>());
  readonly expanded = signal<ReadonlySet<string>>(new Set<string>());
  readonly message = signal('');
  readonly error = signal('');
  readonly busy = signal(false);
  readonly copied = signal(false);

  orgQuery = '';
  orgId = '';
  orgName = '';
  accessExpiry = '';
  expiryInput = '';

  /** Categories flattened to their leaf fields, in schema order. */
  readonly picker = computed<PickerCategory[]>(() =>
    this.categories().map((c) => ({ id: c.id, name: c.name, fields: flatten(c.fields) })),
  );

  readonly selectedCount = computed(() => this.selected().size);

  /** `min` for the datetime inputs, so the browser rejects past dates up front. */
  get nowLocal(): string {
    return toLocalInput(new Date());
  }

  private get userId(): string {
    return this.session.userId();
  }

  constructor() {
    this.loadAll();
    this.searchOrgs();
  }

  // --- Loading ---

  private loadAll(): void {
    forkJoin({
      categories: this.api.getCategorySchema(this.userId).pipe(catchError(() => of([] as Category[]))),
      grants: this.api.listGrants(this.userId).pipe(catchError(() => of([] as Grant[]))),
      codes: this.api.listShareCodes(this.userId).pipe(catchError(() => of([] as ShareCode[]))),
      pending: this.api.listPendingShareRequests(this.userId).pipe(catchError(() => of([] as PendingShareRequest[]))),
    }).subscribe((r) => {
      this.categories.set(r.categories);
      this.grants.set(r.grants);
      this.codes.set(r.codes);
      this.pending.set(r.pending);
    });
  }

  private refreshShares(): void {
    this.api.listGrants(this.userId).subscribe((g) => this.grants.set(g));
    this.api.listShareCodes(this.userId).subscribe((c) => this.codes.set(c));
    this.api.listPendingShareRequests(this.userId).subscribe((p) => this.pending.set(p));
  }

  searchOrgs(): void {
    this.api
      .searchOrganisations(this.orgQuery)
      .pipe(catchError(() => of([] as Organisation[])))
      .subscribe((o) => {
        this.organisations.set(o);
        if (!this.orgQuery) {
          this.orgStatuses.set(new Map(o.map((x) => [x.id, x.status])));
        }
      });
  }

  orgStatus(organisationId: string): string {
    return this.orgStatuses().get(organisationId) ?? '';
  }

  selectOrg(o: Organisation): void {
    this.orgId = o.id;
    this.orgName = o.name;
    this.orgOpen.set(false);
  }

  toggleOrgDropdown(): void {
    const next = !this.orgOpen();
    this.orgOpen.set(next);
    if (next) {
      this.orgQuery = '';
      this.searchOrgs();
    }
  }

  clearOrg(): void {
    this.orgId = '';
    this.orgName = '';
    this.orgQuery = '';
    this.orgOpen.set(false);
    this.searchOrgs();
  }

  // --- Share modal ---

  openShare(): void {
    this.generated.set(null);
    this.error.set('');
    this.copied.set(false);
    this.selected.set(new Set<string>());
    this.orgId = '';
    this.orgName = '';
    this.orgQuery = '';
    this.orgOpen.set(false);
    this.searchOrgs();
    // Default to 30 days out, the shortest window the older preset flow offered.
    const in30Days = new Date();
    in30Days.setDate(in30Days.getDate() + 30);
    this.accessExpiry = toLocalInput(in30Days);
    this.shareOpen.set(true);
  }

  closeShare(): void {
    this.shareOpen.set(false);
    this.generated.set(null);
    this.error.set('');
  }

  toggleExpanded(categoryId: string): void {
    this.expanded.update((prev) => toggle(prev, categoryId));
  }

  isExpanded(categoryId: string): boolean {
    return this.expanded().has(categoryId);
  }

  isSelected(fieldId: string): boolean {
    return this.selected().has(fieldId);
  }

  toggleField(fieldId: string): void {
    this.selected.update((prev) => toggle(prev, fieldId));
  }

  allSelected(c: PickerCategory): boolean {
    return c.fields.length > 0 && c.fields.every((f) => this.selected().has(f.id));
  }

  toggleCategory(c: PickerCategory): void {
    const turnOn = !this.allSelected(c);
    this.selected.update((prev) => {
      const next = new Set(prev);
      for (const f of c.fields) {
        if (turnOn) {
          next.add(f.id);
        } else {
          next.delete(f.id);
        }
      }
      return next;
    });
  }

  canGenerate(): boolean {
    return !!this.orgId && this.selected().size > 0 && !!this.accessExpiry;
  }

  generate(): void {
    if (!this.canGenerate()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.api
      .generateShareCode(this.userId, {
        organisationId: this.orgId,
        fieldDefinitionIds: [...this.selected()],
        accessExpiresAt: new Date(this.accessExpiry).toISOString(),
        scope: 'ReadOnly',
        consentMethod: 'InAppConfirmation',
      })
      .subscribe({
        next: (g) => {
          this.busy.set(false);
          this.generated.set(g);
          this.refreshShares();
        },
        error: (e) => {
          this.busy.set(false);
          this.error.set(apiError(e));
        },
      });
  }

  copyCode(code: string): void {
    navigator.clipboard?.writeText(code).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  // --- Pending requests ---

  approve(r: PendingShareRequest): void {
    this.api.approveShareRequest(this.userId, r.shareCodeId, 'InAppConfirmation').subscribe({
      next: () => {
        this.message.set(`Approved ${r.organisationName}'s request for ${r.fields.length} field(s).`);
        this.refreshShares();
      },
      error: (e) => this.message.set(apiError(e)),
    });
  }

  reject(r: PendingShareRequest): void {
    this.api.rejectShareRequest(this.userId, r.shareCodeId).subscribe({
      next: () => {
        this.message.set(`Rejected ${r.organisationName}'s request.`);
        this.refreshShares();
      },
      error: (e) => this.message.set(apiError(e)),
    });
  }

  // --- Codes and grants ---

  revokeCode(c: ShareCode): void {
    this.api.revokeShareCode(this.userId, c.shareCodeId).subscribe({
      next: () => {
        this.message.set(`Revoked the code for ${c.organisationName}.`);
        this.refreshShares();
      },
      error: (e) => this.message.set(apiError(e)),
    });
  }

  revoke(g: Grant): void {
    this.api.revoke(this.userId, g.grantId).subscribe({
      next: () => {
        this.message.set(`Revoked ${this.categoryName(g.categoryId)} for ${g.organisationName}.`);
        this.refreshShares();
      },
      error: (e) => this.message.set(apiError(e)),
    });
  }

  startEditExpiry(g: Grant): void {
    this.error.set('');
    this.expiryInput = toLocalInput(g.expiresAt ? new Date(g.expiresAt) : new Date());
    this.editingGrant.set(g);
  }

  cancelEditExpiry(): void {
    this.editingGrant.set(null);
  }

  saveExpiry(g: Grant): void {
    this.busy.set(true);
    this.api.changeGrantExpiry(this.userId, g.grantId, new Date(this.expiryInput).toISOString()).subscribe({
      next: () => {
        this.busy.set(false);
        this.editingGrant.set(null);
        this.message.set(`Updated the end date for ${g.organisationName}.`);
        this.refreshShares();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(apiError(e));
      },
    });
  }

  // --- Display helpers ---

  cat(categoryName: string) {
    return categoryMeta(categoryName);
  }

  fieldLabel(fieldName: string): string {
    return fieldMeta(fieldName).displayName;
  }

  categoryName(categoryId: string): string {
    return this.categories().find((c) => c.id === categoryId)?.name ?? '';
  }

  status(g: Grant) {
    return grantStatusMeta(g.status, g.expiresAt);
  }

  codeStatus(status: ShareCodeStatus): { label: string; dot: string; text: string } {
    switch (status) {
      case 'Pending':
        return { label: 'Waiting to be redeemed', dot: 'var(--vid-status-expiring)', text: 'var(--color-text)' };
      case 'AwaitingApproval':
        return { label: 'Awaiting your approval', dot: 'var(--vid-status-expiring)', text: 'var(--vid-status-expiring)' };
      case 'Approved':
        return { label: 'Approved', dot: 'var(--vid-status-ok)', text: 'var(--color-text)' };
      case 'Rejected':
      case 'Revoked':
        return { label: status, dot: 'var(--vid-status-revoked)', text: 'var(--vid-status-revoked)' };
      default:
        return { label: 'Expired', dot: 'var(--vid-status-neutral)', text: 'var(--vid-status-neutral)' };
    }
  }
}

/**
 * Flattens a category's schema to the leaf fields a share can name. A `Group`
 * is a container rather than a value, so only its children are offered.
 */
function flatten(fields: FieldDefinition[]): { id: string; label: string }[] {
  const out: { id: string; label: string }[] = [];
  for (const f of fields) {
    if (f.fieldType === 'Group') {
      for (const child of f.children) {
        out.push({ id: child.id, label: `${fieldMeta(f.name).displayName} · ${fieldMeta(child.name).displayName}` });
      }
    } else {
      out.push({ id: f.id, label: fieldMeta(f.name).displayName });
    }
  }
  return out;
}

function toggle(set: ReadonlySet<string>, value: string): ReadonlySet<string> {
  const next = new Set(set);
  if (!next.delete(value)) {
    next.add(value);
  }
  return next;
}

/** `datetime-local` wants a local-time `YYYY-MM-DDTHH:mm` string, not a UTC instant. */
function toLocalInput(date: Date): string {
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function apiError(e: unknown): string {
  const body = (e as { error?: { detail?: string; title?: string } })?.error;
  return body?.detail ?? body?.title ?? 'Something went wrong. Please try again.';
}
