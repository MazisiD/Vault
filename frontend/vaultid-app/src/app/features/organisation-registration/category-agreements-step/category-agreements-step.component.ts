import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { VaultApiService } from '../../../services/vault-api.service';
import { OrganisationRegistrationStateService } from '../organisation-registration-state.service';

interface CategoryAgreementForm {
  categoryName: string;
  expanded: boolean;
  saved: boolean;
  purpose: string;
  retentionDays: number;
  legalBasis: string;
  thirdPartySharing: string;
  deletionCommitment: string;
}

/**
 * Step 4: optional per-category agreement terms, overriding the org-wide
 * default set in step 3. Skippable in full or per category - anything left
 * unset just falls back to that default, so sharing is never blocked here.
 */
@Component({
  selector: 'app-category-agreements-step',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './category-agreements-step.component.html',
  styleUrl: './category-agreements-step.component.css',
})
export class CategoryAgreementsStepComponent implements OnInit {
  private readonly api = inject(VaultApiService);
  readonly state = inject(OrganisationRegistrationStateService);

  readonly categories = signal<CategoryAgreementForm[]>([]);
  readonly loading = signal(true);
  readonly savingCategory = signal<string | null>(null);
  readonly error = signal('');

  ngOnInit(): void {
    this.api.getCategoryCatalog().subscribe({
      next: (names) => {
        this.categories.set(names.map((categoryName) => ({
          categoryName,
          expanded: false,
          saved: false,
          purpose: '',
          retentionDays: 365,
          legalBasis: '',
          thirdPartySharing: '',
          deletionCommitment: '',
        })));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  toggle(form: CategoryAgreementForm): void {
    form.expanded = !form.expanded;
  }

  async save(form: CategoryAgreementForm): Promise<void> {
    const organisationId = this.state.organisationId();
    if (!organisationId) {
      return;
    }

    this.error.set('');
    this.savingCategory.set(form.categoryName);
    try {
      await firstValueFrom(this.api.setCategoryAgreement(organisationId, {
        categoryName: form.categoryName,
        purpose: form.purpose.trim(),
        retentionDays: form.retentionDays,
        legalBasis: form.legalBasis.trim(),
        thirdPartySharing: form.thirdPartySharing.trim() || null,
        deletionCommitment: form.deletionCommitment.trim() || null,
      }));
      form.saved = true;
      form.expanded = false;
    } catch {
      this.error.set(`Could not save the agreement for '${form.categoryName}'.`);
    } finally {
      this.savingCategory.set(null);
    }
  }

  continue(): void {
    this.state.next();
  }
}
