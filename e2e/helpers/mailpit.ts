import { MAILPIT_URL, SCENARIO_ADDRESSES } from './env';

export interface MailSummary {
  ID: string;
  Subject: string;
  To: { Address: string }[];
}

export interface Mail extends MailSummary {
  Text: string;
}

/** Deletes only the messages sent to the scenario's own addresses; other mail in the shared dev Mailpit is left alone. */
export async function clearMailbox(): Promise<void> {
  for (const address of SCENARIO_ADDRESSES) {
    const res = await fetch(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`to:${address}`)}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`Mailpit search-delete for ${address} answered ${res.status}`);
  }
}

async function list(): Promise<MailSummary[]> {
  const res = await fetch(`${MAILPIT_URL}/api/v1/messages?limit=200`);
  if (!res.ok) throw new Error(`Mailpit GET /api/v1/messages answered ${res.status}`);
  return ((await res.json()) as { messages: MailSummary[] }).messages ?? [];
}

/** Polls Mailpit (the server's outbox worker sends every few seconds) until a mail to `to` with `subject` arrives. */
export async function waitForMail(to: string, subject: string, timeoutMs = 60_000): Promise<Mail> {
  const deadline = Date.now() + timeoutMs;
  let seen: string[] = [];
  while (Date.now() < deadline) {
    let messages: MailSummary[];
    try {
      messages = await list();
    } catch {
      await new Promise((r) => setTimeout(r, 1000));
      continue; // a transient Mailpit failure is retried until the deadline
    }
    seen = messages.map((m) => `${m.To.map((t) => t.Address).join(',')} | ${m.Subject}`);
    const hit = messages.find((m) => m.Subject === subject && m.To.some((t) => t.Address.toLowerCase() === to.toLowerCase()));
    if (hit) {
      const res = await fetch(`${MAILPIT_URL}/api/v1/message/${hit.ID}`);
      if (!res.ok) throw new Error(`Mailpit GET message answered ${res.status}`);
      return (await res.json()) as Mail;
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`No mail to ${to} with subject "${subject}" within ${timeoutMs} ms. Mailbox holds: ${JSON.stringify(seen)}`);
}

/** Pulls the first link whose path starts with `path` out of a plain-text mail body. */
export function linkFrom(mail: Mail, path: string): string {
  const link = (mail.Text.match(/https?:\/\/[^\s<>"')]+/g) ?? []).find((url) => url.includes(path));
  if (!link) throw new Error(`No link containing ${path} in mail "${mail.Subject}": ${mail.Text}`);
  return link;
}
