import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../../services/auth.service';

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            register: jasmine.createSpy('register').and.resolveTo({ ok: true }),
            user: () => null,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
  });

  it('shows the individual sign-up form', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Full name');
    expect(text).toContain('Username');
  });

  it('links out to organisation registration', () => {
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a[routerLink="/register/organisation"]');
    expect(link).toBeTruthy();
  });
});
