import { Component, inject, OnInit } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
})
export class AppComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    void this.auth.waitUntilReady().then(() => {
      this.redirectFromRootIfNeeded();
    });

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe((event) => {
        if (event.urlAfterRedirects === '/' || event.urlAfterRedirects === '') {
          this.redirectFromRootIfNeeded();
        }
      });
  }

  get isOrganisation(): boolean {
    return this.auth.isOrganisation;
  }

  private redirectFromRootIfNeeded(): void {
    if (this.router.url !== '/' && this.router.url !== '') {
      return;
    }

    if (!this.auth.user()) {
      void this.router.navigateByUrl('/login');
      return;
    }

    const target = this.auth.isOrganisation ? '/organisation' : '/vault';
    void this.router.navigateByUrl(target);
  }

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
