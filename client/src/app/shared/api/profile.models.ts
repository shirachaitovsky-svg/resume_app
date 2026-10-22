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
