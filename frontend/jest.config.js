module.exports = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testMatch: ['**/*.spec.ts'],
  transform: {
    '^.+\\.(ts|mjs|js|html)$': [
      'jest-preset-angular',
      {
        tsconfig: '<rootDir>/tsconfig.spec.json',
        stringifyContentPathRegex: '\\.(html|svg)$'
      }
    ]
  },
  transformIgnorePatterns: ['node_modules/(?!.*\\.mjs$|@ionic|@angular|@microsoft/signalr|tslib|ionicons)'],
  moduleNameMapper: {
    '^@core/(.*)$': '<rootDir>/src/app/core/$1',
    '^@features/(.*)$': '<rootDir>/src/app/features/$1',
    '^@shared/(.*)$': '<rootDir>/src/app/shared/$1',
    '^@environments/(.*)$': '<rootDir>/src/environments/$1',
    '^@ionic/angular/standalone$': '<rootDir>/src/__mocks__/ionic-angular-standalone.mock.ts',
    '^ionicons/components$': '<rootDir>/src/__mocks__/ionicons.mock.ts',
    '^ionicons/icons$': '<rootDir>/src/__mocks__/ionicons.mock.ts',
    '^leaflet$': '<rootDir>/src/__mocks__/leaflet.mock.ts'
  },
  collectCoverage: true,
  coverageDirectory: 'coverage',
  coverageReporters: ['text', 'lcov', 'html'],
  coveragePathIgnorePatterns: ['/node_modules/', '/src/environments/']
};
