import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { STRINGS } from './shared/strings.he';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly strings = STRINGS;
}
