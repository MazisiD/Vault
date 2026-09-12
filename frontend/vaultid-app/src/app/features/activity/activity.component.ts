import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { catchError, of } from 'rxjs';
import { VaultApiService } from '../../services/vault-api.service';
import { SessionService } from '../../services/session.service';
import { ActivityEntry, CategorySummary } from '../../models';
import { categoryMeta, eventMeta } from '../../ui-meta';

/**
 * The transparent activity feed (blueprint 4.3, 1): a timeline of every event
 * on the vault - who accessed data and when, every share and revocation. Read
 * straight from the backend; rendered here, nothing computed.
 */
@Component({
  selector: 'app-activity',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './activity.component.html',
  styleUrl: './activity.component.css',
})
export class ActivityComponent {
  private readonly api = inject(VaultApiService);
  private readonly session = inject(SessionService);

  readonly entries = signal<ActivityEntry[]>([]);
  readonly categories = signal<CategorySummary[]>([]);

  constructor() {
    this.reload();
    this.api
      .getVault(this.session.userId())
      .pipe(catchError(() => of(null)))
      .subscribe((v) => this.categories.set(v?.categories ?? []));
  }

  reload(): void {
    this.api.getActivity(this.session.userId()).subscribe((e) => this.entries.set(e));
  }

  meta(eventType: string) {
    return eventMeta(eventType);
  }

  catMeta(categoryId: string) {
    const name = this.categories().find((c) => c.id === categoryId)?.name ?? '';
    return categoryMeta(name);
  }

  tint(eventType: string): string {
    return `color-mix(in srgb, ${eventMeta(eventType).toneColor} 18%, var(--color-surface))`;
  }
}
