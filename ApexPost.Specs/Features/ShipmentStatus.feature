# Copilot-assisted: drafted by GitHub Copilot (prompt 2 in COPILOT-LOG.md), reviewed and edited by the author.
# Raw Copilot output: docs/copilot/raw/prompt2-ShipmentStatus.feature.txt

Feature: Shipment status lifecycle
  As the dispatch team
  I want every shipment to move through the delivery lifecycle in a valid order
  So that customers and couriers always see the real progress of a parcel

  Background:
    Given a shipment "APX-10000001" exists for "Jane Citizen" in "Melbourne"

  Scenario Outline: A shipment advances through the allowed forward path
    Given the shipment has already reached "<from>"
    When the shipment is moved to "<next>"
    Then the shipment status is "<next>"

    Examples:
      | from             | next             |
      | created          | picked up        |
      | picked up        | in transit       |
      | in transit       | out for delivery |
      | out for delivery | delivered        |
      | out for delivery | failed           |

  Scenario: A delivered shipment cannot be moved back to in transit
    Given the shipment has already reached "delivered"
    When the shipment is moved to "in transit"
    Then the status change is refused
    And the shipment status is "delivered"

  Scenario: Skipping a lifecycle step is refused
    When the shipment is moved to "out for delivery"
    Then the status change is refused
    And the shipment status is "created"

  # Added in review: Copilot only covered one refused move per kind; these are the moves most likely to be tried by mistake.
  Scenario Outline: Moves that break the lifecycle are refused
    Given the shipment has already reached "<from>"
    When the shipment is moved to "<to>"
    Then the status change is refused
    And the shipment status is "<from>"

    Examples:
      | from      | to         | why                                |
      | delivered | in transit | a finished delivery cannot reopen  |
      | failed    | delivered  | a failed delivery needs a new trip |
      | created   | delivered  | it never left the depot            |
      | picked up | created    | the courier already has the parcel |

  # Added in review: a late depot scan must never move a delivered parcel backwards.
  Scenario: A late depot scan does not reopen a delivered shipment
    Given the shipment has already reached "delivered"
    When a late "Arrived" scan from "Sydney Depot" is uploaded for the shipment
    Then the late scan is not accepted
    And the shipment status is "delivered"
