Feature: Offline scan sync
  As a courier working where the mobile signal comes and goes
  I want the scans I record on my phone to reach the tracking system when I am back online
  So that customers see accurate progress and no scan is ever lost or counted twice

  Background:
    Given today is 15 December 2026
    And parcel "APX-20000001" is registered for delivery
    And courier "C-1001" records scans on their phone

  Scenario: Offline scans are uploaded when the courier is back online
    Given the courier records these scans while offline:
      | scan      | type      | location        | minutes after 8am |
      | pickup    | Pickup    | Melbourne Depot | 0                 |
      | departure | InTransit | Melbourne Depot | 30                |
    When the phone syncs with the tracking system
    Then 2 scans are confirmed by the tracking system
    And no scans are waiting on the phone
    And parcel "APX-20000001" shows that it is in transit

  Scenario: A scan that already reached the tracking system is not counted twice
    Given the courier recorded a "Pickup" scan called "pickup" at "Melbourne Depot"
    And the scan "pickup" reached the tracking system but the phone never heard back
    When the phone syncs with the tracking system
    Then the scan "pickup" is confirmed on the phone
    And the tracking system holds 1 scan for parcel "APX-20000001"

  Scenario: A delivery without the recipient's signature is rejected and not retried
    Given the courier recorded a "Delivered" scan called "drop-off" at "12 Example Street"
    When the phone syncs with the tracking system
    Then the scan "drop-off" is marked as rejected on the phone because a signature is required
    And no scans are waiting on the phone
    And the tracking system holds 0 scans for parcel "APX-20000001"

  Scenario Outline: Scans stay safely on the phone while the tracking system cannot be reached
    Given the courier recorded <count> pickup scans
    And the tracking system cannot be reached
    When the phone syncs with the tracking system
    Then <count> scans are still waiting on the phone
    And the phone reports that the tracking system is unavailable

    Examples:
      | count |
      | 1     |
      | 5     |
