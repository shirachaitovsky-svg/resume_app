export interface CohortRef {
  readonly id: string;
  readonly name: string;
}

export interface PublicProfileCard {
  readonly id: string;
  readonly name: string;
  readonly productName: string;
  readonly productUrl: string;
  readonly description: string | null;
  readonly avatarId: string | null;
  readonly photoUrl: string | null;
  readonly cohort: CohortRef;
}

export interface IntroAnswer {
  readonly number: number;
  readonly text: string;
}

export interface PublicProfileDetail extends PublicProfileCard {
  readonly role: string | null;
  readonly workplace: string | null;
  readonly introText: string | null;
  readonly introAnswers: readonly IntroAnswer[];
  readonly cv: string | null;
}

export interface ProfileList {
  readonly items: readonly PublicProfileCard[];
  readonly total: number;
}

export interface CohortList {
  readonly items: readonly CohortRef[];
  readonly total: number;
}

export interface ReturnedInfo {
  readonly note: string;
  readonly returnedAt: string;
}

/** The signed-in participant's own profile (`GET /me/profile`). `email` and `phone` already show a pending value. */
export interface OwnProfile {
  readonly name: string;
  readonly productName: string;
  readonly productUrl: string;
  readonly email: string;
  readonly phone: string;
  readonly description: string | null;
  readonly role: string | null;
  readonly workplace: string | null;
  readonly introText: string | null;
  readonly introAnswer1: string | null;
  readonly introAnswer2: string | null;
  readonly introAnswer3: string | null;
  readonly avatarId: string;
  readonly cv: string | null;
  readonly hasPhoto: boolean;
  readonly cohortName: string;
  readonly hasPublished: boolean;
  readonly pending: string | null;
  readonly pendingContactChange: boolean;
  readonly pendingEmail: string | null;
  readonly pendingPhone: string | null;
  readonly returned: ReturnedInfo | null;
}
