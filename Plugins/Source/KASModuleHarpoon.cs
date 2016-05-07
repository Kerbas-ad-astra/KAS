﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KAS {

public class KASModuleHarpoon : KASModuleAttachCore {
  [KSPField] public float forceNeeded = 5;
  [KSPField] public bool attachToPart = true;
  [KSPField] public Vector3 rayDir = Vector3.down;
  [KSPField] public float rayLenght = 1;
  [KSPField] public float partBreakForce = 10;
  [KSPField] public float staticBreakForce = 15;
  [KSPField] public float aboveDist = 0f;

  //Sounds
  [KSPField] public string attachStaticSndPath = "KAS/Sounds/grappleAttachStatic";
  [KSPField] public string attachPartSndPath = "KAS/Sounds/grappleAttachPart";
  [KSPField] public string attachEvaSndPath = "KAS/Sounds/grappleAttachEva";
  [KSPField] public string detachSndPath = "KAS/Sounds/grappleDetach";

  //Info
  [KSPField(guiActive = true, guiName = "State", guiFormat = "S")]
  public string state = "Idle";

  public override string GetInfo() {
    var sb = new StringBuilder();
    sb.AppendFormat("<b>Attach strength (part)</b>: {0:F0}", partBreakForce).AppendLine();
    sb.AppendFormat("<b>Attach strength (ground)</b>: {0:F0}", staticBreakForce).AppendLine();
    sb.AppendFormat("<b>Impact force required</b>: {0:F0}", forceNeeded).AppendLine();
    return sb.ToString();
  }

  public override void OnStart(StartState state) {
    base.OnStart(state);
    if (attachMode.StaticJoint || attachMode.FixedJoint) {
      Events["ContextMenuDetach"].guiActive = true;
      Events["ContextMenuDetach"].guiActiveUnfocused = true;
    } else {
      Events["ContextMenuDetach"].guiActive = false;
      Events["ContextMenuDetach"].guiActiveUnfocused = false;
    }
  }

  public override void OnJointBreakStatic() {
    DetachGrapple();
  }

  public override void OnPartUnpack() {
    base.OnPartUnpack();

    this.part.Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
  }

  public override void OnPartPack() {
    base.OnPartPack();

    this.part.Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
  }

  public void OnKISAction(BaseEventData baseEventData) {
    string action = baseEventData.GetString("action");
    Part tgtPart = (Part)baseEventData.Get("targetPart");

    if (action == "Store" || action == "AttachStart" || action == "DropEnd") {
      DetachGrapple();
    }
    if (action == "AttachEnd") {
      DetachGrapple();
      if (tgtPart == null) {
        AttachStaticGrapple();
      }
    }
  }

  void OnCollisionEnter(Collision collision) {
    if (!attachMode.StaticJoint && !attachMode.FixedJoint) {
      AttachOnCollision(collision);
    }
  }

  private void AttachOnCollision(Collision collision) {
    // Revert precision mode back to the performance. Non-discrete modes are very expensive.
    KAS_Shared.DebugLog(string.Format(
        "AttachOnCollision - Set collision mode back to Discrete on: {0}", part));
    part.Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        
    //Don't attach if inpact force is too low
    if (collision.relativeVelocity.magnitude < forceNeeded)
      return;

    float shorterDist = Mathf.Infinity;
    bool nearestHitFound = false;
    Part nearestHitPart = null;
    RaycastHit nearestHit = new RaycastHit();
    Vector3 rayDirection = this.part.transform.TransformDirection(rayDir);
    //Get all raycast hits in front of the grapple
    var nearestHits = new List<RaycastHit>(
        Physics.RaycastAll(this.part.transform.position, rayDirection, rayLenght, 557059));
    foreach (RaycastHit hit in nearestHits) {
      //Exclude grapple collider
      if (hit.collider == this.part.collider) {
        continue;
      }
      //Exclude parts if needed
      if (!attachToPart && hit.rigidbody && hit.rigidbody.GetComponent<Part>()) {
        continue;
      }

      // Get closest hit
      float tmpShorterDist = Vector3.Distance(this.part.transform.position, hit.point);
      if (tmpShorterDist <= shorterDist) {
        shorterDist = tmpShorterDist;
        nearestHit = hit;
        if (nearestHit.rigidbody) {
          nearestHitPart = nearestHit.rigidbody.GetComponent<Part>();
        }
        nearestHitFound = true;
      }
    }

    if (!nearestHitFound) {
      KAS_Shared.DebugLog("AttachOnCollision - Nothing to attach in front of grapple");
      return;
    }

    KASModuleWinch connectedWinch = KAS_Shared.GetConnectedWinch(this.part);
    if (connectedWinch) {
      MoveAbove(nearestHit.point, nearestHit.normal, aboveDist);
      connectedWinch.cableJointLength = connectedWinch.cableRealLenght;
    }
     
    if (nearestHitPart) {
      KAS_Shared.DebugLog("AttachOnCollision - grappleAttachOnPart=true");
      KAS_Shared.DebugLog("AttachOnCollision - Attaching to part : " + nearestHitPart.partInfo.title); 
      AttachPartGrapple(nearestHitPart);
    } else {
      KAS_Shared.DebugLog("AttachOnCollision - Attaching to static : " + nearestHit.collider.name);
      AttachStaticGrapple();
    }
  }

  public void AttachPartGrapple(Part attachToPart) {
    AttachFixed(this.part, attachToPart, partBreakForce);
    state = "Attached to : " + attachToPart.partInfo.title;
    //Sound
    if (attachToPart.vessel.isEVA) {
      AudioSource.PlayClipAtPoint(GameDatabase.Instance.GetAudioClip(attachEvaSndPath), this.part.transform.position);
    } else {
      AudioSource.PlayClipAtPoint(GameDatabase.Instance.GetAudioClip(attachPartSndPath), this.part.transform.position);
    }
  }

  public void AttachStaticGrapple() {
    AttachStatic(staticBreakForce);
    Events["ContextMenuDetach"].guiActive = true;
    Events["ContextMenuDetach"].guiActiveUnfocused = true;
    state = "Ground attached";
    AudioSource.PlayClipAtPoint(GameDatabase.Instance.GetAudioClip(attachStaticSndPath), this.part.transform.position);
  }
      
  public void DetachGrapple() {
    state = "Idle";
    Events["ContextMenuDetach"].guiActive = false;
    Events["ContextMenuDetach"].guiActiveUnfocused = false;
    if (attachMode.StaticJoint || attachMode.FixedJoint) {
      Detach();
      AudioSource.PlayClipAtPoint(GameDatabase.Instance.GetAudioClip(detachSndPath), this.part.transform.position);
    }
  }

  [KSPEvent(name = "ContextMenuDetach", active = true, guiActive = false, guiActiveUnfocused = false, guiName = "Detach")]
  public void ContextMenuDetach() {
    DetachGrapple();
  }

  [KSPAction("Detach")]
  public void ActionGroupDetach(KSPActionParam param) {
    if (!this.part.packed) {
      DetachGrapple();
    }
  }
}

}  // namespace
