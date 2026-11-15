Feature: Shipment creation
  As a dispatcher at ApexPost Logistics
  I want to register parcels for courier tracking
  So that customers and couriers can follow every parcel from pickup to delivery

  Business rules:
  - A tracking number identifies exactly one parcel
  - A parcel weighs at least 0.1 kg and at most 70 kg
  - Parcels over 30 kg booked from 1 December 2026 pay a heavy-parcel surcharge

  Background:
    Given the tracking service is open for bookings
    And today is 15 December 2026

  Scenario: Registering a valid parcel
    When the dispatcher registers parcel "APX-10000001" for "Test Recipient" in "Melbourne" weighing 2.5 kg
    Then parcel "APX-10000001" is registered and waiting for pickup
    And parcel "APX-10000001" appears when searching for parcels in "Melbourne"

  Scenario: A tracking number can only be used once
    Given parcel "APX-10000001" has already been registered
    When the dispatcher registers parcel "APX-10000001" for "Another Recipient" in "Sydney" weighing 1 kg
    Then the registration is refused because the tracking number is already in use
    And there is exactly 1 parcel with tracking number "APX-10000001"

  Scenario Outline: Parcels outside the weight limits are refused
    When the dispatcher registers parcel "APX-10000002" for "Test Recipient" in "Melbourne" weighing <weight> kg
    Then the registration is refused because the weight is not allowed
    And there is no parcel with tracking number "APX-10000002"

    Examples:
      | weight | why                        |
      | 0.099  | just below the lightest    |
      | 0      | no weight at all           |
      | 70.001 | just above the heaviest    |

  Scenario Outline: Heavy parcels pay a surcharge
    Given parcel "APX-10000003" weighing <weight> kg has been registered
    When the dispatcher checks the charges for parcel "APX-10000003"
    Then the heavy-parcel surcharge is <surcharge>

    Examples:
      | weight | surcharge |
      | 30     | 0.00      |
      | 30.001 | 15.00     |
      | 50     | 15.00     |
      | 50.001 | 25.00     |
      | 70     | 25.00     |

  Scenario: Heavy parcels booked before the surcharge started pay nothing
    Given today is 30 November 2026
    And parcel "APX-10000004" weighing 45 kg has been registered
    And today is 15 December 2026
    When the dispatcher checks the charges for parcel "APX-10000004"
    Then the heavy-parcel surcharge is 0.00
